using System;
using MicroseismicSync.Infrastructure;

namespace MicroseismicSync.Models
{
    public sealed class MonitoredFileItem : ObservableObject
    {
        private string fileName;
        private string fullPath;
        private DateTime creationTime;
        private DateTime lastWriteTime;
        private long fileSizeBytes;
        private string syncStatus;

        public string FileName
        {
            get { return fileName; }
            set { SetProperty(ref fileName, value); }
        }

        public string FullPath
        {
            get { return fullPath; }
            set { SetProperty(ref fullPath, value); }
        }

        public DateTime CreationTime
        {
            get { return creationTime; }
            set { SetProperty(ref creationTime, value); }
        }

        public DateTime LastWriteTime
        {
            get { return lastWriteTime; }
            set { SetProperty(ref lastWriteTime, value); }
        }

        public long FileSizeBytes
        {
            get { return fileSizeBytes; }
            set
            {
                if (SetProperty(ref fileSizeBytes, value))
                {
                    OnPropertyChanged("FileSizeDisplay");
                }
            }
        }

        public string FileSizeDisplay
        {
            get
            {
                if (FileSizeBytes < 1024)
                {
                    return FileSizeBytes + " B";
                }

                if (FileSizeBytes < 1024 * 1024)
                {
                    return (FileSizeBytes / 1024d).ToString("F1") + " KB";
                }

                if (FileSizeBytes < 1024L * 1024L * 1024L)
                {
                    return (FileSizeBytes / 1024d / 1024d).ToString("F1") + " MB";
                }

                return (FileSizeBytes / 1024d / 1024d / 1024d).ToString("F1") + " GB";
            }
        }

        public string SyncStatus
        {
            get { return syncStatus; }
            set { SetProperty(ref syncStatus, value); }
        }
    }
}
