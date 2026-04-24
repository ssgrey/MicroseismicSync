using System;
using System.IO;
using System.Text;

namespace MicroseismicSync.Logging
{
    public sealed class AppLogger : IAppLogger
    {
        private readonly object syncRoot = new object();
        private readonly string logDirectory;

        public AppLogger(AppLogLevel minimumLevel)
        {
            MinimumLevel = minimumLevel;
            logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
        }

        public AppLogLevel MinimumLevel { get; set; }

        public event EventHandler<string> MessageLogged;

        public void Info(string message)
        {
            Write(AppLogLevel.Info, "INFO", message, null);
        }

        public void Debug(string message)
        {
            Write(AppLogLevel.Debug, "DEBUG", message, null);
        }

        public void Error(string message, Exception exception = null)
        {
            Write(AppLogLevel.Error, "ERROR", message, exception);
        }

        private void Write(AppLogLevel level, string levelName, string message, Exception exception)
        {
            if (level < MinimumLevel)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            builder.Append(" | ");
            builder.Append(levelName);
            builder.Append(" | ");
            builder.Append(message ?? string.Empty);

            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }

            var entry = builder.ToString();

            lock (syncRoot)
            {
                File.AppendAllText(GetLogFilePath(), entry + Environment.NewLine, Encoding.UTF8);
            }

            var handler = MessageLogged;
            if (handler != null)
            {
                handler(this, entry);
            }
        }

        private string GetLogFilePath()
        {
            return Path.Combine(logDirectory, "app-" + DateTime.Today.ToString("yyyyMMdd") + ".log");
        }
    }
}
