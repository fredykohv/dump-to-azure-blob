# Azure Blob Storage Uploader

A modern Windows application for uploading files to Azure Blob Storage with a beautiful Material Design interface.

## Features

- Drag and drop file upload
- Progress tracking for each file
- Modern Material Design UI
- Persistent connection settings
- Support for multiple file uploads
- File size display
- Upload status tracking

## Prerequisites

- Windows 10 or later
- .NET 9.0 SDK or later
- Azure Blob Storage account and connection string

## Setup

1. Clone this repository
2. Open the solution in Visual Studio 2022 or later
3. Build and run the application

## Usage

1. Launch the application
2. Enter your Azure Blob Storage connection string and container name
3. Click "Save Settings" to validate and save your connection details
4. Drag and drop files into the application window or click to browse
5. Monitor upload progress in the list view
6. Files will be uploaded to the specified container with their original names

## Security Note

The connection string is stored locally in the application's settings. Make sure to keep your connection string secure and never share it with others.

## License

MIT License 