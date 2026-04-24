using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MicroseismicSync.Bootstrap;
using MicroseismicSync.Logging;

namespace MicroseismicSync
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        private IAppLogger logger;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            try
            {
                var bootstrapper = new AppBootstrapper();
                var mainWindow = await bootstrapper.CreateMainWindowAsync(e.Args);
                logger = bootstrapper.Logger;

                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "MicroseismicSync Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            logger?.Error("Unhandled UI exception.", e.Exception);
            MessageBox.Show(
                e.Exception.ToString(),
                "Unhandled UI Exception",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger?.Error("Unhandled AppDomain exception.", e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            logger?.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        }
    }
}
