using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using MicroseismicSync.Infrastructure;
using MicroseismicSync.Logging;
using MicroseismicSync.Models;
using Forms = System.Windows.Forms;

namespace MicroseismicSync.ViewModels
{
    public sealed class FileMonitorPanelViewModel : ObservableObject, IDisposable
    {
        private readonly string searchPattern;
        private readonly IAppLogger logger;
        private readonly object syncRoot = new object();
        private readonly DispatcherTimer rescanTimer;
        private readonly DispatcherTimer applyFolderPathTimer;
        private readonly Dictionary<string, FileSnapshot> fileSnapshots;
        private FileSystemWatcher watcher;
        private string folderPath;
        private string folderPathInput;
        private bool suppressFolderPathInputAutoApply;

        public FileMonitorPanelViewModel(string title, string searchPattern, IAppLogger logger)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new ArgumentException("title");
            }

            if (string.IsNullOrWhiteSpace(searchPattern))
            {
                throw new ArgumentException("searchPattern");
            }

            this.searchPattern = searchPattern;
            this.logger = logger;
            Title = title;
            Files = new ObservableCollection<MonitoredFileItem>();
            fileSnapshots = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
            rescanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            rescanTimer.Tick += OnRescanTimerTick;
            applyFolderPathTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
            };
            applyFolderPathTimer.Tick += OnApplyFolderPathTimerTick;

            SelectFolderCommand = new RelayCommand(SelectFolder);
        }

        public string Title { get; private set; }

        public string FolderPath
        {
            get { return folderPath; }
            private set { SetProperty(ref folderPath, value); }
        }

        public string FolderPathInput
        {
            get { return folderPathInput; }
            set
            {
                if (SetProperty(ref folderPathInput, value) && !suppressFolderPathInputAutoApply)
                {
                    ScheduleFolderPathApply();
                }
            }
        }

        public ObservableCollection<MonitoredFileItem> Files { get; private set; }

        public RelayCommand SelectFolderCommand { get; private set; }

        public void Dispose()
        {
            StopWatcher();
            rescanTimer.Stop();
            rescanTimer.Tick -= OnRescanTimerTick;
            applyFolderPathTimer.Stop();
            applyFolderPathTimer.Tick -= OnApplyFolderPathTimerTick;
        }

        private void SelectFolder()
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择" + Title + "文件夹";
                dialog.ShowNewFolderButton = true;

                var currentPath = NormalizeFolderPath(FolderPathInput);
                if (!Directory.Exists(currentPath))
                {
                    currentPath = NormalizeFolderPath(FolderPath);
                }

                dialog.SelectedPath = Directory.Exists(currentPath) ? currentPath : string.Empty;

                if (dialog.ShowDialog() != Forms.DialogResult.OK)
                {
                    return;
                }

                StartMonitoring(dialog.SelectedPath);
            }
        }

        private void StartMonitoring(string path)
        {
            var normalizedPath = NormalizeFolderPath(path);
            applyFolderPathTimer.Stop();

            if (string.Equals(normalizedPath, NormalizeFolderPath(FolderPath), StringComparison.OrdinalIgnoreCase))
            {
                UpdateFolderPathInput(normalizedPath);
                return;
            }

            if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(normalizedPath))
            {
                logger.Info(string.Format("{0} monitor path rejected: {1}", Title, path ?? string.Empty));
                return;
            }

            StopWatcher();

            FolderPath = normalizedPath;
            UpdateFolderPathInput(normalizedPath);
            ReloadFiles();
            StartWatcher(normalizedPath);
            StartRescan(normalizedPath);

            logger.Info(string.Format("{0} monitor started: {1} ({2})", Title, normalizedPath, searchPattern));
        }

        private void StartWatcher(string path)
        {
            try
            {
                watcher = new FileSystemWatcher(path, searchPattern);
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
                watcher.IncludeSubdirectories = false;
                watcher.Created += OnCreated;
                watcher.Changed += OnChanged;
                watcher.Deleted += OnDeleted;
                watcher.Renamed += OnRenamed;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                watcher = null;
                logger.Error(string.Format("{0} file watcher unavailable, polling will be used: {1}", Title, path), ex);
            }
        }

        private void StartRescan(string path)
        {
            if (IsNetworkPath(path) || watcher == null)
            {
                rescanTimer.Stop();
                rescanTimer.Start();
                return;
            }

            rescanTimer.Stop();
        }

        private void StopWatcher()
        {
            rescanTimer.Stop();

            if (watcher == null)
            {
                return;
            }

            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnCreated;
            watcher.Changed -= OnChanged;
            watcher.Deleted -= OnDeleted;
            watcher.Renamed -= OnRenamed;
            watcher.Dispose();
            watcher = null;
        }

        private void ReloadFiles()
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            var currentPath = FolderPath;
            var fileItems = new List<MonitoredFileItem>();
            var snapshots = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Directory.Exists(currentPath))
                {
                    fileItems = Directory
                        .EnumerateFiles(currentPath, searchPattern, SearchOption.TopDirectoryOnly)
                        .Select(path => CreateItem(path, "待同步"))
                        .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var item in fileItems)
                    {
                        snapshots[item.FullPath] = CaptureSnapshot(item.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(string.Format("{0} monitor reload failed: {1}", Title, currentPath), ex);
            }

            Action update = delegate
            {
                lock (syncRoot)
                {
                    Files.Clear();
                    foreach (var item in fileItems)
                    {
                        Files.Add(item);
                    }

                    fileSnapshots.Clear();
                    foreach (var entry in snapshots)
                    {
                        fileSnapshots[entry.Key] = entry.Value;
                    }
                }
            };

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                dispatcher.Invoke(update);
            }
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            UpsertFile(e.FullPath, "待同步");
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            UpsertFile(e.FullPath, "已变更");
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            RemoveFile(e.FullPath, true);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            RemoveFile(e.OldFullPath, true);
            UpsertFile(e.FullPath, "待同步");
        }

        private void OnRescanTimerTick(object sender, EventArgs e)
        {
            RefreshByScanning();
        }

        private void OnApplyFolderPathTimerTick(object sender, EventArgs e)
        {
            applyFolderPathTimer.Stop();
            StartMonitoring(FolderPathInput);
        }

        private void RefreshByScanning()
        {
            var currentPath = FolderPath;
            var currentFiles = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Directory.Exists(currentPath))
                {
                    foreach (var filePath in Directory.EnumerateFiles(currentPath, searchPattern, SearchOption.TopDirectoryOnly))
                    {
                        currentFiles[filePath] = CaptureSnapshot(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(string.Format("{0} monitor scan failed: {1}", Title, currentPath), ex);
                return;
            }

            List<string> addedPaths;
            List<string> removedPaths;
            List<string> changedPaths;

            lock (syncRoot)
            {
                addedPaths = currentFiles.Keys
                    .Where(path => !fileSnapshots.ContainsKey(path))
                    .ToList();

                removedPaths = fileSnapshots.Keys
                    .Where(path => !currentFiles.ContainsKey(path))
                    .ToList();

                changedPaths = currentFiles.Keys
                    .Where(path => fileSnapshots.ContainsKey(path) && !fileSnapshots[path].Equals(currentFiles[path]))
                    .ToList();
            }

            foreach (var path in removedPaths)
            {
                RemoveFile(path, false);
            }

            foreach (var path in addedPaths)
            {
                UpsertFile(path, "待同步");
            }

            foreach (var path in changedPaths)
            {
                UpsertFile(path, "已变更");
            }
        }

        private void RemoveFile(string path, bool writeLog)
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            Action update = delegate
            {
                lock (syncRoot)
                {
                    var existing = Files.FirstOrDefault(item =>
                        string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        Files.Remove(existing);
                    }

                    fileSnapshots.Remove(path);
                }
            };

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                dispatcher.BeginInvoke(update);
            }

            if (writeLog)
            {
                logger.Info(string.Format("{0} removed: {1}", Title, Path.GetFileName(path)));
            }
        }

        private void UpsertFile(string path, string status)
        {
            if (!File.Exists(path))
            {
                return;
            }

            var snapshot = CaptureSnapshot(path);
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            Action update = delegate
            {
                lock (syncRoot)
                {
                    var existing = Files.FirstOrDefault(item =>
                        string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase));

                    if (existing == null)
                    {
                        Files.Add(CreateItem(path, status));
                        SortFiles();
                    }
                    else
                    {
                        existing.CreationTime = SafeGetCreationTime(path);
                        existing.LastWriteTime = SafeGetLastWriteTime(path);
                        existing.FileSizeBytes = SafeGetFileSize(path);
                        existing.SyncStatus = status;
                    }

                    fileSnapshots[path] = snapshot;
                }
            };

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                update();
            }
            else
            {
                dispatcher.BeginInvoke(update);
            }

            logger.Info(string.Format("{0} detected: {1}", Title, Path.GetFileName(path)));
        }

        private MonitoredFileItem CreateItem(string path, string status)
        {
            return new MonitoredFileItem
            {
                FileName = Path.GetFileName(path),
                FullPath = path,
                CreationTime = SafeGetCreationTime(path),
                LastWriteTime = SafeGetLastWriteTime(path),
                FileSizeBytes = SafeGetFileSize(path),
                SyncStatus = status,
            };
        }

        private void SortFiles()
        {
            var ordered = Files
                .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var index = 0; index < ordered.Count; index++)
            {
                var item = ordered[index];
                var currentIndex = Files.IndexOf(item);
                if (currentIndex >= 0 && currentIndex != index)
                {
                    Files.Move(currentIndex, index);
                }
            }
        }

        private static string NormalizeFolderPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Trim('"');
        }

        private void ScheduleFolderPathApply()
        {
            applyFolderPathTimer.Stop();

            if (string.IsNullOrWhiteSpace(NormalizeFolderPath(FolderPathInput)))
            {
                return;
            }

            applyFolderPathTimer.Start();
        }

        private void UpdateFolderPathInput(string path)
        {
            suppressFolderPathInputAutoApply = true;

            try
            {
                FolderPathInput = path;
            }
            finally
            {
                suppressFolderPathInputAutoApply = false;
            }
        }

        private static bool IsNetworkPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
        }

        private static FileSnapshot CaptureSnapshot(string path)
        {
            return new FileSnapshot(
                SafeGetCreationTime(path),
                SafeGetLastWriteTime(path),
                SafeGetFileSize(path));
        }

        private static DateTime SafeGetLastWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTime(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static DateTime SafeGetCreationTime(string path)
        {
            try
            {
                return File.GetCreationTime(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static long SafeGetFileSize(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        private struct FileSnapshot
        {
            public FileSnapshot(DateTime creationTime, DateTime lastWriteTime, long fileSizeBytes)
            {
                CreationTime = creationTime;
                LastWriteTime = lastWriteTime;
                FileSizeBytes = fileSizeBytes;
            }

            public DateTime CreationTime { get; private set; }

            public DateTime LastWriteTime { get; private set; }

            public long FileSizeBytes { get; private set; }
        }
    }
}
