using System;
using System.IO;

namespace CodectoryCore.Logging
{
    public enum LogEntryType
    {
        Info,
        Warning,
        Error
    }

    public sealed class LogEntry
    {
        public LogEntry(DateTime date, LogEntryType entryType, string content)
        {
            Date = date;
            EntryType = entryType;
            Content = content ?? string.Empty;
        }

        public LogEntry(string content, LogEntryType entryType = LogEntryType.Info)
            : this(DateTime.Now, entryType, content)
        {
        }

        public DateTime Date { get; private set; }
        public string Content { get; private set; }
        public LogEntryType EntryType { get; private set; }

        public override string ToString()
        {
            return string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", Date, EntryType, Content);
        }
    }

    public sealed class Logs : IDisposable
    {
        private readonly object _sync = new object();

        public Logs(string logPath, string applicationName, string version, bool mainInstanceLoggingEnabled, bool logFileEnabled = true)
        {
            LogPath = logPath;
            MainInstanceLoggingEnabled = mainInstanceLoggingEnabled;
            LogFileEnabled = logFileEnabled;
        }

        public Logs(string logPath, string applicationName, int version, bool mainInstanceLoggingEnabled)
            : this(logPath, applicationName, version.ToString(), mainInstanceLoggingEnabled)
        {
        }

        public bool MainInstanceLoggingEnabled { get; set; }
        public bool LogFileEnabled { get; set; }
        public bool Disposed { get; private set; }
        public string LogPath { get; private set; }

        public event EventHandler<Exception> ExternalExceptionLog;
        public event EventHandler<LogEntry> NewLog;

        public void Add(string content, bool mainInstanceOnly)
        {
            Add(content, mainInstanceOnly, LogEntryType.Info);
        }

        public void Add(string content, bool mainInstanceOnly, LogEntryType entryType)
        {
            if (mainInstanceOnly && !MainInstanceLoggingEnabled)
                return;
            AppendLogEntry(new LogEntry(content, entryType));
        }

        public void AddException(string message, Exception exception, bool mainInstanceOnly = false)
        {
            Add(message + Environment.NewLine + exception, mainInstanceOnly, LogEntryType.Error);
            if (ExternalExceptionLog != null)
                ExternalExceptionLog(this, exception);
        }

        public void AddException(Exception exception, bool mainInstanceOnly = false)
        {
            AddException(exception.Message, exception, mainInstanceOnly);
        }

        public void AppendLogEntry(LogEntry entry)
        {
            if (Disposed)
                return;
            if (LogFileEnabled)
            {
                lock (_sync)
                {
                    string folder = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(folder))
                        Directory.CreateDirectory(folder);
                    File.AppendAllText(LogPath, entry + Environment.NewLine);
                }
            }
            if (NewLog != null)
                NewLog(this, entry);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
