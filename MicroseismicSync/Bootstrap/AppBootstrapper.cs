using System;
using System.Configuration;
using System.Net;
using System.Threading.Tasks;
using MicroseismicSync.Infrastructure;
using MicroseismicSync.Logging;
using MicroseismicSync.Services;
using MicroseismicSync.ViewModels;

namespace MicroseismicSync.Bootstrap
{
    public sealed class AppBootstrapper
    {
        public IAppLogger Logger { get; private set; }

        public async Task<MainWindow> CreateMainWindowAsync(string[] args)
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls;

            var logger = new AppLogger(ReadLogLevelSetting());
            Logger = logger;

            var container = new ServiceRegistry();
            var parser = new LaunchContextParser();
            var launchContext = parser.Parse(args);
            var allowInvalidCertificate = ReadBooleanSetting("AllowInvalidServerCertificate", true);
            var timeout = TimeSpan.FromSeconds(ReadIntSetting("HttpTimeoutSeconds", 300));
            var autoLoadWells = ReadBooleanSetting("AutoLoadWellsOnStartup", true);

            var apiClient = new ApiClient(logger, allowInvalidCertificate, timeout);
            var wellDataService = new WellDataService(apiClient, logger);
            var mainViewModel = new MainViewModel(apiClient, wellDataService, logger);

            container.RegisterSingleton(logger);
            container.RegisterSingleton<IAppLogger>(logger);
            container.RegisterSingleton(launchContext);
            container.RegisterSingleton<IApiClient>(apiClient);
            container.RegisterSingleton<IWellDataService>(wellDataService);
            container.RegisterSingleton(mainViewModel);

            await mainViewModel.InitializeAsync(launchContext, autoLoadWells);

            return new MainWindow
            {
                DataContext = mainViewModel,
            };
        }

        private static bool ReadBooleanSetting(string key, bool defaultValue)
        {
            bool value;
            return bool.TryParse(ConfigurationManager.AppSettings[key], out value) ? value : defaultValue;
        }

        private static int ReadIntSetting(string key, int defaultValue)
        {
            int value;
            return int.TryParse(ConfigurationManager.AppSettings[key], out value) ? value : defaultValue;
        }

        private static AppLogLevel ReadLogLevelSetting()
        {
            var configuredValue = ConfigurationManager.AppSettings["LogLevel"];
            AppLogLevel level;
            if (!string.IsNullOrWhiteSpace(configuredValue) &&
                Enum.TryParse(configuredValue, true, out level))
            {
                return level;
            }

            return ReadBooleanSetting("DebugLogging", true)
                ? AppLogLevel.Debug
                : AppLogLevel.Info;
        }
    }
}
