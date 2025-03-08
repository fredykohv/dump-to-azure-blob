# Azure Blob Storage Manager

A Windows application for managing your Azure Blob Storage

## Features

- Drag and drop file upload
- Progress tracking for each file
- Persistent connection settings
- Support for multiple file uploads
- File size display
- Upload status tracking
- Download files from Azure Blob Storage
- Monitor folders (Changes inside selected folder(s) echo in Azure Blob Storage)

## Prerequisites

- Windows 11 or later
- .NET 9.0 SDK or later
- Azure Blob Storage account, container, connection string

## Setup

1. Clone this repository
2. Open the solution in Visual Studio 2022 or later
3. Build and run the application

## First launch

1. Launch the application
2. Enter your Azure Blob Storage connection string and container name
3. Click "Save Settings" to validate and save your connection details

## Upload

1. Drag and drop files into the application window
2. Monitor upload progress in the list view
3. Files will be uploaded to the specified container with their original names

## Folder Monitoring

1. Copy-paste your folder path or browse the folder location
2. Click on 'Add Folder'
3. Changes (File Changed/Deleted/Added) will echo in your Azure Blob Storage Container

## Download

1. When settings are correct, a list of files in your Azure Blob Storage Container should appear. If not, click refresh.
2. Click on 'Download' to download the desired file.

## Security Note

The connection string is stored locally in the application's settings. Make sure to keep your connection string secure and never share it with others.
