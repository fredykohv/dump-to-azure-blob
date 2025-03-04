using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Win32;
using System.Collections.Specialized;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using System.Text.Json;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using System.Runtime.InteropServices;

namespace DumpToAzureBlob
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<FileUploadItem> _uploadItems;
        private ObservableCollection<string> _monitoredFolders;
        private BlobServiceClient? _blobServiceClient;
        private string? _containerName;
        private readonly Dictionary<string, FileSystemWatcher> _folderWatchers;
        private const string MonitoredFoldersFile = "monitored_folders.json";

        public MainWindow()
        {
            InitializeComponent();
            _uploadItems = new ObservableCollection<FileUploadItem>();
            _monitoredFolders = new ObservableCollection<string>();
            _folderWatchers = new Dictionary<string, FileSystemWatcher>();
            
            FilesListView.ItemsSource = _uploadItems;
            MonitoredFoldersListView.ItemsSource = _monitoredFolders;

            // Load saved settings
            LoadSettings();
        }

        private void LoadSettings()
        {
            ConnectionStringTextBox.Text = Properties.Settings.Default.ConnectionString;
            ContainerNameTextBox.Text = Properties.Settings.Default.ContainerName;

            _blobServiceClient = new BlobServiceClient(ConnectionStringTextBox.Text);
            _containerName = ContainerNameTextBox.Text;

            LoadMonitoredFolders();
        }

        private void LoadMonitoredFolders()
        {
            try
            {
                if (File.Exists(MonitoredFoldersFile))
                {
                    var json = File.ReadAllText(MonitoredFoldersFile);
                    var folders = JsonSerializer.Deserialize<string[]>(json);
                    if (folders != null)
                    {
                        foreach (var folder in folders)
                        {
                            if (Directory.Exists(folder))
                            {
                                _monitoredFolders.Add(folder);
                                StartWatchingFolder(folder);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading monitored folders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveMonitoredFolders()
        {
            try
            {
                var json = JsonSerializer.Serialize(_monitoredFolders.ToArray());
                File.WriteAllText(MonitoredFoldersFile, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving monitored folders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.ConnectionString = ConnectionStringTextBox.Text;
            Properties.Settings.Default.ContainerName = ContainerNameTextBox.Text;
            Properties.Settings.Default.Save();

            try
            {
                _blobServiceClient = new BlobServiceClient(ConnectionStringTextBox.Text);
                _containerName = ContainerNameTextBox.Text;
                MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                MessageBox.Show("Folder browser is only supported on Windows", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FolderPathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = FolderPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(folderPath))
            {
                MessageBox.Show("Please enter a folder path", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!Directory.Exists(folderPath))
            {
                MessageBox.Show("The specified folder does not exist", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_monitoredFolders.Contains(folderPath))
            {
                MessageBox.Show("This folder is already being monitored", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _monitoredFolders.Add(folderPath);
            StartWatchingFolder(folderPath);
            SaveMonitoredFolders();
            FolderPathTextBox.Clear();
        }

        private void RemoveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.DataContext is string folderPath)
            {
                StopWatchingFolder(folderPath);
                _monitoredFolders.Remove(folderPath);
                SaveMonitoredFolders();
            }
        }

        private void StartWatchingFolder(string folderPath)
        {
            if (_blobServiceClient == null || string.IsNullOrEmpty(_containerName))
            {
                MessageBox.Show("Please configure Azure Blob Storage settings before adding folders to monitor", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(folderPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    Filter = "*.*",
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = true
                };

                // Only handle Changed event for files that are done being written
                watcher.Changed += async (s, e) => await OnFileChangedAsync(s, e);
                watcher.Deleted += async (s, e) => await OnFileDeletedAsync(s, e);
                watcher.Renamed += async (s, e) => await OnFileRenamedAsync(s, e);

                _folderWatchers[folderPath] = watcher;
                _ = SyncFolderWithBlobStorageAsync(folderPath);
                
                MessageBox.Show($"Started monitoring folder: {folderPath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting folder monitoring: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopWatchingFolder(string folderPath)
        {
            if (_folderWatchers.TryGetValue(folderPath, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _folderWatchers.Remove(folderPath);
            }
        }

        private async Task SyncFolderWithBlobStorageAsync(string folderPath)
        {
            if (_blobServiceClient == null || string.IsNullOrEmpty(_containerName))
            {
                MessageBox.Show("Azure Blob Storage settings not configured", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                
                MessageBox.Show($"Found {files.Length} files to sync in {folderPath}", "Information", MessageBoxButton.OK, MessageBoxImage.Information);

                foreach (var file in files)
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(folderPath, file);
                        var blockBlobClient = containerClient.GetBlockBlobClient(relativePath);
                        
                        // Check if the blob already exists
                        if (await blockBlobClient.ExistsAsync())
                        {
                            // Get the local file's last modified time
                            var localFileInfo = new FileInfo(file);
                            var localLastModified = localFileInfo.LastWriteTimeUtc;
                            
                            // Get the blob's last modified time
                            var blobProperties = await blockBlobClient.GetPropertiesAsync();
                            var blobLastModified = blobProperties.Value.LastModified.UtcDateTime;
                            
                            // Only upload if the local file is newer
                            if (localLastModified <= blobLastModified)
                            {
                                // Skip
                                continue;
                            }
                        }

                        using var fileStream = File.OpenRead(file);
                        await blockBlobClient.UploadAsync(fileStream, new BlobUploadOptions());
                        MessageBox.Show($"Successfully uploaded: {relativePath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error uploading file {file}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error syncing folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task OnFileChangedAsync(object sender, FileSystemEventArgs e)
        {
            if (_blobServiceClient == null || string.IsNullOrEmpty(_containerName))
            {
                MessageBox.Show("Azure Blob Storage settings not configured", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Check if the file is still being written to
                using (var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // If we can open the file with ReadWrite sharing, it's done being written
                    var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                    var relativePath = Path.GetRelativePath(Path.GetDirectoryName(e.FullPath)!, e.FullPath);
                    var blockBlobClient = containerClient.GetBlockBlobClient(relativePath);

                    await blockBlobClient.UploadAsync(fs, new BlobUploadOptions());
                    MessageBox.Show($"Successfully uploaded changed file: {relativePath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error uploading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task OnFileDeletedAsync(object sender, FileSystemEventArgs e)
        {
            if (_blobServiceClient == null || string.IsNullOrEmpty(_containerName))
                return;

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var relativePath = Path.GetRelativePath(Path.GetDirectoryName(e.FullPath)!, e.FullPath);
                var blockBlobClient = containerClient.GetBlockBlobClient(relativePath);
                await blockBlobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting blob: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task OnFileRenamedAsync(object sender, RenamedEventArgs e)
        {
            // Delete the old file
            await OnFileDeletedAsync(sender, e);
            // Wait for the Changed event to handle the new file
        }

        private void FilesListView_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
        }

        private void FilesListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string file in files)
                {
                    AddFileForUpload(file);
                }
            }
        }

        private void AddFileForUpload(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var uploadItem = new FileUploadItem
            {
                FileName = fileInfo.Name,
                FilePath = filePath,
                Size = FormatFileSize(fileInfo.Length),
                Status = "Pending"
            };

            _uploadItems.Add(uploadItem);
            UploadFileAsync(uploadItem);
        }

        private async void UploadFileAsync(FileUploadItem item)
        {
            if (_blobServiceClient == null || string.IsNullOrEmpty(_containerName))
            {
                item.Status = "Error: Settings not configured";
                return;
            }

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
                var blockBlobClient = containerClient.GetBlockBlobClient(Path.GetFileName(item.FilePath));

                item.Status = "Uploading...";

                using var fileStream = File.OpenRead(item.FilePath);
                var fileSize = new FileInfo(item.FilePath).Length;
                var progress = new Progress<long>(bytesTransferred =>
                {
                    item.Progress = (double)bytesTransferred / fileSize * 100;
                });

                await blockBlobClient.UploadAsync(fileStream, new BlobUploadOptions { ProgressHandler = progress });
                
                item.Status = "Completed";
                item.Progress = 100;
                MessageBox.Show($"Successfully uploaded: {item.FileName}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                item.Status = $"Error: {ex.Message}";
                MessageBox.Show($"Error uploading {item.FileName}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            foreach (var watcher in _folderWatchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            base.OnClosing(e);
        }
    }

    public class FileUploadItem : INotifyPropertyChanged
    {
        private string _fileName = string.Empty;
        private string _filePath = string.Empty;
        private string _size = string.Empty;
        private string _status = string.Empty;
        private double _progress;

        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public string Size
        {
            get => _size;
            set => SetProperty(ref _size, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
} 