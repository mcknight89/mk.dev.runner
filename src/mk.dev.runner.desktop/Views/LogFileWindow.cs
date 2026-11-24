using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using mk.dev.runner.core.Logs;
using mk.dev.runner.desktop.Controls;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop.Views
{
    public sealed class LogFileWindow : Window
    {
        private readonly string _path;
        private readonly LogLines _lines = [];
        private readonly DispatcherTimer _timer;
        private long _readTo;
        private long _lineNumber;
        private DateTime _day = DateTime.Today;

        public LogFileWindow(string title, string path)
        {
            _path = path;
            Title = title;
            Style = (Style)Application.Current.FindResource("DarkWindow");
            Width = 1100;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current.MainWindow;
            Content = new LogView
            {
                Lines = _lines,
                FilePath = path,
                EmptyText = File.Exists(path) ? "The log file is empty." : "No log file yet. It's written while the service runs.",
            };
            ReadNew();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => ReadNew();
            _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        private void ReadNew()
        {
            if (!File.Exists(_path))
                return;
            try
            {
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < _readTo)
                {
                    _readTo = 0;
                    _lineNumber = 0;
                    _lines.Clear();
                }
                if (fs.Length == _readTo)
                    return;
                fs.Seek(_readTo, SeekOrigin.Begin);
                var buffer = new byte[fs.Length - _readTo];
                var got = fs.Read(buffer, 0, buffer.Length);
                var end = Array.LastIndexOf(buffer, (byte)'\n', got - 1);
                if (end < 0)
                    return;
                _readTo += end + 1;
                _day = File.GetLastWriteTime(_path).Date;
                var fresh = Encoding.UTF8.GetString(buffer, 0, end + 1)
                    .Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .Where(l => l.Length > 0)
                    .Select(Parse)
                    .ToList();
                _lines.AddMany(fresh);
            }
            catch (IOException)
            {
            }
        }

        private LogLine Parse(string text)
        {
            _lineNumber++;
            if (text.Length > 13 && text[12] == ' '
                && TimeSpan.TryParseExact(text[..12], @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var time))
                return new LogLine(_lineNumber, _day + time, text[13..]);
            return new LogLine(_lineNumber, default, text);
        }
    }
}
