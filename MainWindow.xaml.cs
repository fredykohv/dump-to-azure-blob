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

namespace DumpToAzureBlob
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<FileUploadItem> _uploadItems;
        private BlobServiceClient? _blobServiceClient;
        private string? _containerName;

        public MainWindow()
        {
            InitializeComponent();
            _uploadItems = new ObservableCollection<FileUploadItem>();
            FilesListView.ItemsSource = _uploadItems;

            // Load saved settings
            LoadSettings();
        }

        private void LoadSettings()
        {
            ConnectionStringTextBox.Text = Properties.Settings.Default.ConnectionString;
            ContainerNameTextBox.Text = Properties.Settings.Default.ContainerName;
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
            }
            catch (Exception ex)
            {
                item.Status = $"Error: {ex.Message}";
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