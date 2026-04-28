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
        // 本地文件自动同步开关。
        private bool isAutoSyncEnabled;
        // 同一时刻只允许一个同步请求在跑，避免并发上传打乱顺序。
        private bool isSyncInProgress;
        // 同步执行期间如果又检测到新文件，用这个标记在当前任务结束后继续补跑下一轮。
        private bool hasPendingSyncRequest;
        private bool isBackendMonitoring;
        private bool isBackendRefreshInProgress;
        private bool hasPendingBackendRefresh;
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

            // 文件新增/变更时先做一次短暂防抖，避免同一批文件触发过多同步请求。
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

            // 以当前目录中的文件作为基线，自动同步只处理启动后新出现的文件。
            SgyPanel.BeginAutoSyncTracking();
            EsfPanel.BeginAutoSyncTracking();
            CsvPanel.BeginAutoSyncTracking();
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
            SgyPanel.EndAutoSyncTracking();
            EsfPanel.EndAutoSyncTracking();
            CsvPanel.EndAutoSyncTracking();
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

            if (!isAutoSyncEnabled || isSyncInProgress || !HasAutoSyncFilesToProcess())
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
            // 自动同步严格按队列顺序一次只处理一个文件。
            var nextFile = GetNextAutoSyncFile();
            if (nextFile == null)
            {
                StatusMessage = "当前没有待同步文件。";
                logger.Info(StatusMessage);
                return;
            }

            await SyncFilesAsync(
                new[] { nextFile },
                true,
                "同步完成，成功 {0}，失败 {1}。");
        }

        public async Task SyncSelectedFilesAsync(string fileType, IEnumerable<MonitoredFileItem> selectedFiles)
        {
            if (selectedFiles == null)
            {
                StatusMessage = "未选择要同步的文件。";
                logger.Info(StatusMessage);
                return;
            }

            // 手动同步按当前选择批量执行，但仍然复用统一的上传和状态更新逻辑。
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

        private async Task SyncFilesAsync(IList<SyncFileItem> files, bool continueAutoSyncAfterCompletion, string resultMessageFormat)
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
                // 同步顺序由调用方决定；这里按传入顺序串行调用接口。
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

                StatusMessage = string.Format(resultMessageFormat, successCount, failedCount);
                logger.Info(StatusMessage);
            }
            finally
            {
                isSyncInProgress = false;
                SetBusy(false, string.Empty);

                // 自动模式下，如果同步期间又进了新文件，或者队列里还有待处理文件，继续下一轮。
                if (continueAutoSyncAfterCompletion && (hasPendingSyncRequest || HasAutoSyncFilesToProcess()))
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

        private SyncFileItem GetNextAutoSyncFile()
        {
            return EnumerateAutoSyncFiles(SgyPanel)
                .Concat(EnumerateAutoSyncFiles(EsfPanel))
                .Concat(EnumerateAutoSyncFiles(CsvPanel))
                // 自动同步按文件产生顺序排队：越早创建的文件越先上传。
                .OrderBy(item => item.File.CreationTime)
                .ThenBy(item => item.File.LastWriteTime)
                .ThenBy(item => item.File.FileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private bool HasAutoSyncFilesToProcess()
        {
            return EnumerateAutoSyncFiles(SgyPanel)
                .Concat(EnumerateAutoSyncFiles(EsfPanel))
                .Concat(EnumerateAutoSyncFiles(CsvPanel))
                .Any();
        }

        private static IEnumerable<SyncFileItem> EnumerateAutoSyncFiles(FileMonitorPanelViewModel panel)
        {
            // 自动同步只处理真正还在队列里的文件，已经同步成功的文件不再重复进入队列。
            return panel.Files
                .Where(file => file.IsAutoSyncCandidate &&
                    (string.Equals(file.SyncStatus, "待同步", StringComparison.Ordinal) ||
                     string.Equals(file.SyncStatus, "已变更", StringComparison.Ordinal)))
                .Select(file => new SyncFileItem(panel.Title, file));
        }

        private static IEnumerable<SyncFileItem> EnumeratePendingFiles(FileMonitorPanelViewModel panel)
        {
            // 手动同步允许重新触发失败项或未完成项，因此只排除已同步文件。
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
                // 文件列表发生入队/出队变化后，请求一次自动同步调度。
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

            // 重新启动防抖定时器，合并短时间内的连续文件事件。
            syncDebounceTimer.Stop();
            syncDebounceTimer.Start();
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
