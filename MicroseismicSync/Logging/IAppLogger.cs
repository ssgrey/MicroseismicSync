using System;

namespace MicroseismicSync.Logging
{
    public interface IAppLogger
    {
        AppLogLevel MinimumLevel { get; set; }

        event EventHandler<string> MessageLogged;

        void Info(string message);

        void Debug(string message);

        void Error(string message, Exception exception = null);
    }
}
