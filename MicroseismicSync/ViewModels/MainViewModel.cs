using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MicroseismicSync.Infrastructure;
using MicroseismicSync.Logging;
using MicroseismicSync.Models;
using MicroseismicSync.Services;

namespace MicroseismicSync.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly IApiClient apiClient;
        private readonly IWellDataService wellDataService;
        private readonly IAppLogger logger;
        private readonly Queue<string> logBuffer;
        private readonly DispatcherTimer syncDebounceTimer;
        private readonly DispatcherTimer backendMonitorTimer;
        private readonly AsyncRelayCommand loadWellsCommand;
        private readonly RelayCommand startSyncCommand;
        private readonly RelayCommand stopSyncCommand;
        private readonly AsyncRelayCommand startBackendMonitorCommand;
        private readonly RelayCommand stopBackendMonitorCommand;
        private readonly RelayCommand openBackendMonitorSettingsCommand;
        private readonly EventHandler<string> logMessageHandler;

        private string baseUrl;
        private string authorizationToken;
        private string tetProjectId;
        private string projectName;
        private string startupArgument;
        private string statusMessage;
        private string busyMessage;
        private string logText;
        private bool isBusy;
        private bool isLogPanelVisible;
        private bool isAutoSyncEnabled;
        private bool isSyncInProgress;
        private bool hasPendingSyncRequest;
        private bool isBackendMonitoring;
        private bool isBackendRefreshInProgress;
        private bool hasPendingBackendRefresh;
        private int sgyFileCount;
        private int esfFileCount;
        private int csvFileCount;
        private int activeTabIndex;
        private int backendMonitorIntervalSeconds;
        private WellInfo selectedWell;
        private WellInfo selectedBackendWell;

        public MainViewModel(IApiClient apiClient, IWellDataService wellDataService, IAppLogger logger)
        {
            this.apiClient = apiClient;
            this.wellDataService = wellDataService;
            this.logger = logger;

            Wells = new ObservableCollection<WellInfo>();
            logBuffer = new Queue<string>();
            backendMonitorIntervalSeconds = 15;

            SgyPanel = new FileMonitorPanelViewModel("SGY", "*.sgy", logger);
            EsfPanel = new FileMonitorPanelViewModel("ESF", "*.esf", logger);
            CsvPanel = new FileMonitorPanelViewModel("CSV", "*.csv", logger);

            BackendSgyPanel = new StoredFilePanelViewModel("SGY");
            BackendEsfPanel = new StoredFilePanelViewModel("ESF");
            BackendCsvPanel = new StoredFilePanelViewModel("CSV");

            loadWellsCommand = new AsyncRelayCommand(LoadWellsAsync, CanLoadWells);
            startSyncCommand = new RelayCommand(StartSync, CanStartSync);
            stopSyncCommand = new RelayCommand(StopSync, CanStopSync);
            startBackendMonitorCommand = new AsyncRelayCommand(StartBackendMonitoringAsync, CanStartBackendMonitoring);
            stopBackendMonitorCommand = new RelayCommand(StopBackendMonitoring, CanStopBackendMonitoring);
            openBackendMonitorSettingsCommand = new RelayCommand(OpenBackendMonitorSettings, CanOpenBackendMonitorSettings);

            LoadWellsCommand = loadWellsCommand;
            StartSyncCommand = startSyncCommand;
            StopSyncCommand = stopSyncCommand;
            StartBackendMonitorCommand = startBackendMonitorCommand;
            StopBackendMonitorCommand = stopBackendMonitorCommand;
            OpenBackendMonitorSettingsCommand = openBackendMonitorSettingsCommand;
            ToggleLogPanelCommand = new RelayCommand(ToggleLogPanel);
            ClearLogCommand = new RelayCommand(ClearLogPanel);

            syncDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800),
            };
            syncDebounceTimer.Tick += OnSyncDebounceTimerTick;

            backendMonitorTimer = new DispatcherTimer();
            backendMonitorTimer.Tick += OnBackendMonitorTimerTick;
            UpdateBackendMonitorTimerInterval();

            SubscribeToPanelChanges();
            logMessageHandler = OnLoggerMessageLogged;
            logger.MessageLogged += logMessageHandler;

            apiClient.RequestStarted += OnRequestStarted;
            apiClient.RequestCompleted += OnRequestCompleted;
            apiClient.RequestFailed += OnRequestFailed;
        }

        public event EventHandler LogEntriesUpdated;

        public ICommand LoadWellsCommand { get; private set; }

        public ICommand StartSyncCommand { get; private set; }

        public ICommand StopSyncCommand { get; private set; }

        public ICommand StartBackendMonitorCommand { get; private set; }

        public ICommand StopBackendMonitorCommand { get; private set; }

        public ICommand OpenBackendMonitorSettingsCommand { get; private set; }

        public ICommand ToggleLogPanelCommand { get; private set; }

        public ICommand ClearLogCommand { get; private set; }

        public ObservableCollection<WellInfo> Wells { get; private set; }

        public FileMonitorPanelViewModel SgyPanel { get; private set; }

        public FileMonitorPanelViewModel EsfPanel { get; private set; }

        public FileMonitorPanelViewModel CsvPanel { get; private set; }

        public StoredFilePanelViewModel BackendSgyPanel { get; private set; }

        public StoredFilePanelViewModel BackendEsfPanel { get; private set; }

        public StoredFilePanelViewModel BackendCsvPanel { get; private set; }

        public string BaseUrl
        {
            get { return baseUrl; }
            set { SetProperty(ref baseUrl, value); }
        }

        public string AuthorizationToken
        {
            get { return authorizationToken; }
            set { SetProperty(ref authorizationToken, value); }
        }

        public string TetProjectId
        {
            get { return tetProjectId; }
            set { SetProperty(ref tetProjectId, value); }
        }

        public string ProjectName
        {
            get { return projectName; }
            set
            {
                if (SetProperty(ref projectName, value))
                {
                    OnPropertyChanged("DisplayProjectName");
                }
            }
        }

        public string StartupArgument
        {
            get { return startupArgument; }
            set { SetProperty(ref startupArgument, value); }
        }

        public string StatusMessage
        {
            get { return statusMessage; }
            private set { SetProperty(ref statusMessage, value); }
        }

        public string BusyMessage
        {
            get { return busyMessage; }
            private set { SetProperty(ref busyMessage, value); }
        }

        public string LogText
        {
            get { return logText; }
            private set { SetProperty(ref logText, value); }
        }

        public bool IsBusy
        {
            get { return isBusy; }
            private set { SetProperty(ref isBusy, value); }
        }

        public bool IsLogPanelVisible
        {
            get { return isLogPanelVisible; }
            private set
            {
                if (SetProperty(ref isLogPanelVisible, value))
                {
                    OnPropertyChanged("LogToggleGlyph");
                }
            }
        }

        public string LogToggleGlyph
        {
            get { return IsLogPanelVisible ? "▼" : "▲"; }
        }

        public WellInfo SelectedWell
        {
            get { return selectedWell; }
            set
            {
                if (SetProperty(ref selectedWell, value))
                {
                    OnPropertyChanged("SelectedWellText");
                    UpdateCommandStates();
                }
            }
        }

        public WellInfo SelectedBackendWell
        {
            get { return selectedBackendWell; }
            set
            {
                if (SetProperty(ref selectedBackendWell, value))
                {
                    OnPropertyChanged("SelectedBackendWellText");
                    UpdateCommandStates();

                    if (IsBackendMonitoring)
                    {
                        BeginBackendRefresh();
                    }
                }
            }
        }

        public int ActiveTabIndex
        {
            get { return activeTabIndex; }
            set
            {
                if (SetProperty(ref activeTabIndex, value))
                {
                    OnPropertyChanged("IsUploadTabActive");
                    OnPropertyChanged("IsBackendTabActive");
                    UpdateCommandStates();
                }
            }
        }

        public bool IsUploadTabActive
        {
            get { return ActiveTabIndex == 0; }
        }

        public bool IsBackendTabActive
        {
            get { return ActiveTabIndex == 1; }
        }

        public string DisplayProjectName
        {
            get
            {
                return string.IsNullOrWhiteSpace(ProjectName)
                    ? "当前工程：未识别"
                    : "当前工程：" + ProjectName;
            }
        }

        public string SelectedWellText
        {
            get
            {
                return SelectedWell == null
                    ? "未选择井"
                    : "已选择：" + ResolveWellDisplayName(SelectedWell);
            }
        }

        public string SelectedBackendWellText
        {
            get
            {
                return SelectedBackendWell == null
                    ? "未选择井"
                    : "已选择：" + ResolveWellDisplayName(SelectedBackendWell);
            }
        }

        public bool CanChangeSelectedWell
        {
            get { return !isAutoSyncEnabled; }
        }

        public bool CanChangeSelectedBackendWell
        {
            get { return !IsBackendMonitoring; }
        }

        public string BackendMonitorStatusText
        {
            get
            {
                return IsBackendMonitoring
                    ? string.Format("监控中，间隔 {0} 秒", BackendMonitorIntervalSeconds)
                    : string.Format("未监控，间隔 {0} 秒", BackendMonitorIntervalSeconds);
            }
        }

        public bool IsBackendMonitoring
        {
            get { return isBackendMonitoring; }
            private set
            {
                if (SetProperty(ref isBackendMonitoring, value))
                {
                    OnPropertyChanged("BackendMonitorStatusText");
                    OnPropertyChanged("CanChangeSelectedBackendWell");
                    UpdateCommandStates();
                }
            }
        }

        public int BackendMonitorIntervalSeconds
        {
            get { return backendMonitorIntervalSeconds; }
            private set
            {
                if (SetProperty(ref backendMonitorIntervalSeconds, value))
                {
                    UpdateBackendMonitorTimerInterval();
                    OnPropertyChanged("BackendMonitorStatusText");
                }
            }
        }

        public async Task InitializeAsync(ApiLaunchContext launchContext, bool autoLoadWells)
        {
            BaseUrl = launchContext.BaseUrl;
            AuthorizationToken = launchContext.Token;
            TetProjectId = launchContext.TetProjectId;
            ProjectName = launchContext.ProjectName;
            StartupArgument = launchContext.RawArgument;

            ApplyConnectionSettings();
            logger.Info("Application initialized.");

            if (!string.IsNullOrWhiteSpace(StartupArgument))
            {
                logger.Debug("Startup argument detected: " + StartupArgument);
            }

            StatusMessage = "准备就绪。";
            UpdateCommandStates();

            if (autoLoadWells && HasConnectionSettings())
            {
                await LoadWellsAsync();
            }
        }

        public void Dispose()
        {
            syncDebounceTimer.Stop();
            syncDebounceTimer.Tick -= OnSyncDebounceTimerTick;

            backendMonitorTimer.Stop();
            backendMonitorTimer.Tick -= OnBackendMonitorTimerTick;

            SgyPanel.Files.CollectionChanged -= OnPanelFilesCollectionChanged;
            EsfPanel.Files.CollectionChanged -= OnPanelFilesCollectionChanged;
            CsvPanel.Files.CollectionChanged -= OnPanelFilesCollectionChanged;
            logger.MessageLogged -= logMessageHandler;
            apiClient.RequestStarted -= OnRequestStarted;
            apiClient.RequestCompleted -= OnRequestCompleted;
            apiClient.RequestFailed -= OnRequestFailed;

            SgyPanel.Dispose();
            EsfPanel.Dispose();
            CsvPanel.Dispose();
        }

        public IReadOnlyList<string> GetLogEntriesSnapshot()
        {
            return logBuffer.ToArray();
        }

        private async Task LoadWellsAsync()
        {
            if (!HasConnectionSettings())
            {
                StatusMessage = "缺少接口配置，无法获取井列表。";
                logger.Info(StatusMessage);
                return;
            }

            SetBusy(true, "正在获取井列表...");
            ApplyConnectionSettings();

            try
            {
                var result = await wellDataService.GetWellsAsync();
                var orderedWells = result
                    .OrderBy(well => well.WellName ?? string.Empty)
                    .ThenBy(well => well.BoreholeName ?? string.Empty)
                    .ToList();

                Wells.Clear();
                foreach (var well in orderedWells)
                {
                    Wells.Add(well);
                }

                SelectedWell = Wells.FirstOrDefault();
                SelectedBackendWell = Wells.FirstOrDefault();
                StatusMessage = orderedWells.Count == 0
                    ? "未返回井数据。"
                    : "井列表获取完成。";

                logger.Info(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = "井列表获取失败，请检查日志。";
                logger.Error(StatusMessage, ex);
            }
            finally
            {
                SetBusy(false, string.Empty);
                UpdateCommandStates();
            }
        }

        private void StartSync()
        {
            if (!CanStartSync())
            {
                return;
            }

            CaptureCurrentFileCounts();
            hasPendingSyncRequest = false;
            isAutoSyncEnabled = true;
            syncDebounceTimer.Stop();
            StatusMessage = "同步监控已启动。";
            logger.Info(StatusMessage);
            OnPropertyChanged("CanChangeSelectedWell");
            UpdateCommandStates();
        }

        private void StopSync()
        {
            if (!isAutoSyncEnabled)
            {
                return;
            }

            isAutoSyncEnabled = false;
            hasPendingSyncRequest = false;
            syncDebounceTimer.Stop();
            StatusMessage = "同步监控已停止。";
            logger.Info(StatusMessage);
            OnPropertyChanged("CanChangeSelectedWell");
            UpdateCommandStates();
        }

        private async Task StartBackendMonitoringAsync()
        {
            if (!CanStartBackendMonitoring())
            {
                return;
            }

            IsBackendMonitoring = true;
            hasPendingBackendRefresh = false;
            backendMonitorTimer.Start();
            StatusMessage = "后端监控已启动。";
            logger.Info(StatusMessage);

            await RefreshBackendFilesAsync();
        }

        private void StopBackendMonitoring()
        {
            if (!IsBackendMonitoring)
            {
                return;
            }

            IsBackendMonitoring = false;
            hasPendingBackendRefresh = false;
            backendMonitorTimer.Stop();
            StatusMessage = "后端监控已停止。";
            logger.Info(StatusMessage);
        }

        private void OpenBackendMonitorSettings()
        {
            var window = new MonitorSettingsWindow(BackendMonitorIntervalSeconds)
            {
                Owner = Application.Current != null ? Application.Current.MainWindow : null,
            };

            var result = window.ShowDialog();
            if (result != true)
            {
                return;
            }

            BackendMonitorIntervalSeconds = window.IntervalSeconds;
            StatusMessage = string.Format("后端监控间隔已设置为 {0} 秒。", BackendMonitorIntervalSeconds);
            logger.Info(StatusMessage);
        }

        private async void OnSyncDebounceTimerTick(object sender, EventArgs e)
        {
            syncDebounceTimer.Stop();

            if (!isAutoSyncEnabled || isSyncInProgress || !HasFileCountChanged())
            {
                return;
            }

            await SyncPendingFilesAsync();
        }

        private async void OnBackendMonitorTimerTick(object sender, EventArgs e)
        {
            await RefreshBackendFilesAsync();
        }

        private async Task SyncPendingFilesAsync()
        {
            var files = GetFilesToSync().ToList();
            var currentSgyCount = SgyPanel.Files.Count;
            var currentEsfCount = EsfPanel.Files.Count;
            var currentCsvCount = CsvPanel.Files.Count;

            if (files.Count == 0)
            {
                UpdateRecordedFileCounts(currentSgyCount, currentEsfCount, currentCsvCount);
                StatusMessage = "文件数量已变化，当前没有待同步文件。";
                logger.Info(StatusMessage);
                return;
            }

            await SyncFilesAsync(files, true, "同步完成，成功 {0}，失败 {1}。");
        }

        public async Task SyncSelectedFilesAsync(string fileType, IEnumerable<MonitoredFileItem> selectedFiles)
        {
            if (selectedFiles == null)
            {
                StatusMessage = "未选择要同步的文件。";
                logger.Info(StatusMessage);
                return;
            }

            var files = selectedFiles
                .Where(file => file != null)
                .GroupBy(file => file.FullPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(file => new SyncFileItem(fileType, file))
                .ToList();

            if (files.Count == 0)
            {
                StatusMessage = "未选择要同步的文件。";
                logger.Info(StatusMessage);
                return;
            }

            await SyncFilesAsync(files, false, "手动同步完成，成功 {0}，失败 {1}。");
        }

        private async Task SyncFilesAsync(IList<SyncFileItem> files, bool updateRecordedFileCounts, string resultMessageFormat)
        {
            if (SelectedWell == null)
            {
                StatusMessage = "未选择井，无法同步。";
                logger.Info(StatusMessage);
                return;
            }

            if (!HasConnectionSettings())
            {
                StatusMessage = "缺少接口配置，无法同步。";
                logger.Info(StatusMessage);
                return;
            }

            if (isSyncInProgress)
            {
                StatusMessage = "当前正在同步，请稍后再试。";
                logger.Info(StatusMessage);
                return;
            }

            isSyncInProgress = true;
            SetBusy(true, "正在同步文件...");
            ApplyConnectionSettings();

            var successCount = 0;
            var failedCount = 0;

            try
            {
                foreach (var syncItem in files)
                {
                    syncItem.File.SyncStatus = "同步中";

                    try
                    {
                        if (!File.Exists(syncItem.File.FullPath))
                        {
                            syncItem.File.SyncStatus = "文件不存在";
                            failedCount++;
                            logger.Info("File skipped because it does not exist: " + syncItem.File.FullPath);
                            continue;
                        }

                        var request = new CreateStyleFileRequest
                        {
                            Name = BuildStyleFileName(SelectedWell, syncItem.FileType, syncItem.File.FileName),
                            FilePath = syncItem.File.FullPath,
                        };

                        var result = await wellDataService.CreateStyleFileAsync(request);
                        if (result)
                        {
                            syncItem.File.SyncStatus = "已同步";
                            successCount++;
                            logger.Info("File synchronized: " + request.Name);
                        }
                        else
                        {
                            syncItem.File.SyncStatus = "同步失败";
                            failedCount++;
                            logger.Info("File synchronization returned false: " + request.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        syncItem.File.SyncStatus = "同步失败";
                        failedCount++;
                        logger.Error("Failed to synchronize file: " + syncItem.File.FullPath, ex);
                    }
                }

                if (updateRecordedFileCounts)
                {
                    UpdateRecordedFileCounts(SgyPanel.Files.Count, EsfPanel.Files.Count, CsvPanel.Files.Count);
                }

                StatusMessage = string.Format(resultMessageFormat, successCount, failedCount);
                logger.Info(StatusMessage);
            }
            finally
            {
                isSyncInProgress = false;
                SetBusy(false, string.Empty);

                if (hasPendingSyncRequest)
                {
                    hasPendingSyncRequest = false;
                    RequestAutoSync();
                }
            }
        }

        private async Task RefreshBackendFilesAsync()
        {
            if (!IsBackendMonitoring || SelectedBackendWell == null)
            {
                return;
            }

            if (isBackendRefreshInProgress)
            {
                hasPendingBackendRefresh = true;
                return;
            }

            isBackendRefreshInProgress = true;
            SetBusy(true, "正在获取后端文件...");
            ApplyConnectionSettings();

            try
            {
                var allFiles = await wellDataService.GetStyleFileListAsync(new GetStyleFileListRequest());
                var parsedFiles = allFiles
                    .Select(ParseStoredFile)
                    .Where(item => item != null)
                    .Where(item => IsForSelectedBackendWell(item.WellName))
                    .ToList();

                BackendSgyPanel.SetFiles(parsedFiles
                    .Where(item => string.Equals(item.FileType, BackendSgyPanel.Title, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.ToStoredStyleFileItem()));

                BackendEsfPanel.SetFiles(parsedFiles
                    .Where(item => string.Equals(item.FileType, BackendEsfPanel.Title, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.ToStoredStyleFileItem()));

                BackendCsvPanel.SetFiles(parsedFiles
                    .Where(item => string.Equals(item.FileType, BackendCsvPanel.Title, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.ToStoredStyleFileItem()));

                StatusMessage = "后端文件列表获取完成。";
                logger.Info(StatusMessage);
            }
            catch (Exception ex)
            {
                StatusMessage = "后端文件列表获取失败，请检查日志。";
                logger.Error(StatusMessage, ex);
            }
            finally
            {
                isBackendRefreshInProgress = false;
                SetBusy(false, string.Empty);

                if (hasPendingBackendRefresh && IsBackendMonitoring)
                {
                    hasPendingBackendRefresh = false;
                    BeginBackendRefresh();
                }
            }
        }

        private void BeginBackendRefresh()
        {
            if (!IsBackendMonitoring)
            {
                return;
            }

            if (isBackendRefreshInProgress)
            {
                hasPendingBackendRefresh = true;
                return;
            }

            FireAndForget(RefreshBackendFilesAsync());
        }

        private static ParsedStoredFileItem ParseStoredFile(StyleFileInfo file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.Name))
            {
                return null;
            }

            var parts = file.Name.Split(new[] { '@' }, 3);
            if (parts.Length < 3)
            {
                return null;
            }

            return new ParsedStoredFileItem
            {
                WellName = parts[0],
                FileType = parts[1],
                FileName = parts[2],
                CreateTime = file.CreateTime,
            };
        }

        private bool IsForSelectedBackendWell(string wellName)
        {
            var candidates = GetWellNameCandidates(SelectedBackendWell);
            return candidates.Any(candidate => string.Equals(candidate, wellName, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> GetWellNameCandidates(WellInfo well)
        {
            return new[]
            {
                well == null ? null : well.WellName,
                well == null ? null : well.Uwi,
                well == null ? null : well.WellNumber,
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private void ApplyConnectionSettings()
        {
            apiClient.SetBaseUrl(BaseUrl);
            apiClient.SetHeaders(AuthorizationToken, TetProjectId);
        }

        private bool HasConnectionSettings()
        {
            return !string.IsNullOrWhiteSpace(BaseUrl);
        }

        private bool CanLoadWells()
        {
            return HasConnectionSettings();
        }

        private bool CanStartSync()
        {
            return IsUploadTabActive && !isAutoSyncEnabled && SelectedWell != null && HasConnectionSettings();
        }

        private bool CanStopSync()
        {
            return IsUploadTabActive && isAutoSyncEnabled;
        }

        private bool CanStartBackendMonitoring()
        {
            return IsBackendTabActive && !IsBackendMonitoring && SelectedBackendWell != null && HasConnectionSettings();
        }

        private bool CanStopBackendMonitoring()
        {
            return IsBackendTabActive && IsBackendMonitoring;
        }

        private bool CanOpenBackendMonitorSettings()
        {
            return IsBackendTabActive;
        }

        private IEnumerable<SyncFileItem> GetFilesToSync()
        {
            return EnumeratePendingFiles(SgyPanel)
                .Concat(EnumeratePendingFiles(EsfPanel))
                .Concat(EnumeratePendingFiles(CsvPanel));
        }

        private static IEnumerable<SyncFileItem> EnumeratePendingFiles(FileMonitorPanelViewModel panel)
        {
            return panel.Files
                .Where(file => !string.Equals(file.SyncStatus, "已同步", StringComparison.Ordinal))
                .Select(file => new SyncFileItem(panel.Title, file));
        }

        private static string ResolveWellDisplayName(WellInfo well)
        {
            if (well == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(well.WellName) ? well.Uwi : well.WellName;
        }

        private static string BuildStyleFileName(WellInfo well, string fileType, string fileName)
        {
            return string.Join("@", new[]
            {
                ResolveWellDisplayName(well),
                fileType ?? string.Empty,
                fileName ?? string.Empty,
            });
        }

        private void SubscribeToPanelChanges()
        {
            SgyPanel.Files.CollectionChanged += OnPanelFilesCollectionChanged;
            EsfPanel.Files.CollectionChanged += OnPanelFilesCollectionChanged;
            CsvPanel.Files.CollectionChanged += OnPanelFilesCollectionChanged;
        }

        private void OnPanelFilesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!isAutoSyncEnabled)
            {
                return;
            }

            if (e.Action == NotifyCollectionChangedAction.Add ||
                e.Action == NotifyCollectionChangedAction.Remove ||
                e.Action == NotifyCollectionChangedAction.Reset)
            {
                RequestAutoSync();
            }
        }

        private void RequestAutoSync()
        {
            if (!isAutoSyncEnabled)
            {
                return;
            }

            if (isSyncInProgress)
            {
                hasPendingSyncRequest = true;
                return;
            }

            syncDebounceTimer.Stop();
            syncDebounceTimer.Start();
        }

        private void CaptureCurrentFileCounts()
        {
            UpdateRecordedFileCounts(SgyPanel.Files.Count, EsfPanel.Files.Count, CsvPanel.Files.Count);
        }

        private void UpdateRecordedFileCounts(int newSgyCount, int newEsfCount, int newCsvCount)
        {
            sgyFileCount = newSgyCount;
            esfFileCount = newEsfCount;
            csvFileCount = newCsvCount;
        }

        private bool HasFileCountChanged()
        {
            return sgyFileCount != SgyPanel.Files.Count ||
                   esfFileCount != EsfPanel.Files.Count ||
                   csvFileCount != CsvPanel.Files.Count;
        }

        private void UpdateBackendMonitorTimerInterval()
        {
            backendMonitorTimer.Interval = TimeSpan.FromSeconds(BackendMonitorIntervalSeconds);
        }

        private void ToggleLogPanel()
        {
            IsLogPanelVisible = !IsLogPanelVisible;
        }

        private void ClearLogPanel()
        {
            logBuffer.Clear();
            LogText = string.Empty;
            RaiseLogEntriesUpdated();
            logger.Info("Log panel cleared.");
        }

        private void OnLoggerMessageLogged(object sender, string entry)
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                AppendLogEntry(entry);
                return;
            }

            dispatcher.BeginInvoke(new Action<string>(AppendLogEntry), entry);
        }

        private void AppendLogEntry(string entry)
        {
            logBuffer.Enqueue(entry);

            while (logBuffer.Count > 500)
            {
                logBuffer.Dequeue();
            }

            LogText = string.Join(Environment.NewLine, logBuffer);
            RaiseLogEntriesUpdated();
        }

        private void RaiseLogEntriesUpdated()
        {
            var handler = LogEntriesUpdated;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void SetBusy(bool busy, string message)
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action<bool, string>(SetBusy), busy, message);
                return;
            }

            IsBusy = busy;
            BusyMessage = message;
            UpdateCommandStates();
        }

        private void UpdateCommandStates()
        {
            var dispatcher = Application.Current != null ? Application.Current.Dispatcher : null;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(UpdateCommandStates));
                return;
            }

            loadWellsCommand.RaiseCanExecuteChanged();
            startBackendMonitorCommand.RaiseCanExecuteChanged();
            CommandManager.InvalidateRequerySuggested();
        }

        private static async void FireAndForget(Task task)
        {
            await task;
        }

        private void OnRequestStarted(object sender, string operation)
        {
            SetBusy(true, operation);
        }

        private void OnRequestCompleted(object sender, string operation)
        {
            SetBusy(false, string.Empty);
        }

        private void OnRequestFailed(object sender, string operation)
        {
            SetBusy(false, string.Empty);
        }

        private sealed class SyncFileItem
        {
            public SyncFileItem(string fileType, MonitoredFileItem file)
            {
                FileType = fileType;
                File = file;
            }

            public string FileType { get; private set; }

            public MonitoredFileItem File { get; private set; }
        }

        private sealed class ParsedStoredFileItem
        {
            public string WellName { get; set; }

            public string FileType { get; set; }

            public string FileName { get; set; }

            public DateTime CreateTime { get; set; }

            public StoredStyleFileItem ToStoredStyleFileItem()
            {
                return new StoredStyleFileItem
                {
                    FileName = FileName,
                    CreateTime = CreateTime,
                };
            }
        }
    }
}
