using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Timer = System.Windows.Forms.Timer;

namespace MainClient.LogViewer
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Windows.Forms;

    public class LogItem
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;
        public Color Color { get; set; }

        public string[] CachedLines { get; set; } = Array.Empty<string>();
        public int LineCount => CachedLines.Length == 0 ? 1 : CachedLines.Length;
    }

    public class LogViewerUltra : Control
    {
        private readonly List<LogItem> _logs = new();
        private readonly List<LogItem> _filteredLogs = new();
        private readonly ConcurrentQueue<LogItem> _queue = new();
        private readonly object _lock = new();

        private readonly VScrollBar _scrollBar = new();
        private readonly Timer _timer = new();

        private readonly Dictionary<Color, SolidBrush> _brushCache = new();

        private int _lineHeight = 18;
        private bool _autoScroll = true;
        private bool _paused;
        private string _filter = string.Empty;
        private bool _filterDirty = true;

        private int _totalLines;

        private int _selectStartLine = -1;
        private int _selectEndLine = -1;
        private bool _selecting;

        private readonly List<(LogItem log, int startLine, int lineCount)> _logLines = new();

        public int MaxLogs { get; set; } = 300_000;

        public LogViewerUltra()
        {
            DoubleBuffered = true;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);

            Font = new Font("Microsoft YaHei UI", 9);

            _scrollBar.Dock = DockStyle.Right;
            _scrollBar.Width = 16;
            _scrollBar.Scroll += (s, e) =>
            {
                _autoScroll = _scrollBar.Value >= _scrollBar.Maximum - _scrollBar.LargeChange;
                Invalidate();
            };
            Controls.Add(_scrollBar);

            _timer.Interval = 80;
            _timer.Tick += (s, e) => FlushQueue();
            _timer.Start();

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            MouseWheel += LogViewer_MouseWheel;
            Resize += (s, e) => UpdateScrollBar();
            MouseDown += LogViewer_MouseDown;
            MouseMove += LogViewer_MouseMove;
            MouseUp += LogViewer_MouseUp;
            KeyDown += LogViewer_KeyDown;
        }

        public void WriteLog(string message, LogLevel level)
        {
            var safeMessage = message ?? string.Empty;
            var prefix = $"{DateTime.Now:HH:mm:ss} [{level}] ";
            var merged = prefix + safeMessage;

            var item = new LogItem
            {
                Time = DateTime.Now,
                Level = level,
                Message = safeMessage,
                Color = GetColor(level),
                CachedLines = NormalizeLines(merged)
            };

            _queue.Enqueue(item);
        }

        public void Pause() => _paused = true;
        public void Resume() => _paused = false;

        public void ClearLogs()
        {
            lock (_lock)
            {
                _logs.Clear();
                _filteredLogs.Clear();
                _logLines.Clear();
                _totalLines = 0;
            }

            _selectStartLine = -1;
            _selectEndLine = -1;

            UpdateScrollBar();
            Invalidate();
        }

        public string GetAllLogs()
        {
            lock (_lock)
            {
                return string.Join(Environment.NewLine,
                    _logs.Select(x => $"{x.Time:HH:mm:ss} [{x.Level}] {x.Message}"));
            }
        }

        public void SetFilter(string text)
        {
            var filter = text ?? string.Empty;
            if (string.Equals(_filter, filter, StringComparison.Ordinal))
                return;

            _filter = filter;
            _filterDirty = true;

            ApplyFilterIfNeeded();
            UpdateScrollBar();
            Invalidate();
        }

        private static string[] NormalizeLines(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new[] { string.Empty };

            return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        private void FlushQueue()
        {
            if (_paused)
                return;

            var hadUpdates = false;
            var dequeued = 0;

            while (_queue.TryDequeue(out var log))
            {
                hadUpdates = true;
                lock (_lock)
                {
                    _logs.Add(log);
                    if (_logs.Count > MaxLogs)
                    {
                        var remove = _logs.Count - MaxLogs;
                        _logs.RemoveRange(0, remove);
                    }
                }

                dequeued++;
                if (dequeued >= 8000)
                    break;
            }

            if (!hadUpdates && !_filterDirty)
                return;

            ApplyFilterIfNeeded();
            UpdateScrollBar();

            if (_autoScroll)
                _scrollBar.Value = _scrollBar.Maximum;

            Invalidate();
        }

        private void ApplyFilterIfNeeded()
        {
            lock (_lock)
            {
                if (!_filterDirty && _filteredLogs.Count == _logs.Count && string.IsNullOrEmpty(_filter))
                    return;

                _filteredLogs.Clear();
                if (string.IsNullOrEmpty(_filter))
                {
                    _filteredLogs.AddRange(_logs);
                }
                else
                {
                    _filteredLogs.AddRange(_logs.Where(x => x.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase)));
                }

                _logLines.Clear();
                _totalLines = 0;
                foreach (var log in _filteredLogs)
                {
                    var lineCount = Math.Max(1, log.LineCount);
                    _logLines.Add((log, _totalLines, lineCount));
                    _totalLines += lineCount;
                }

                _filterDirty = false;
            }
        }

        private void LogViewer_MouseWheel(object sender, MouseEventArgs e)
        {
            var delta = e.Delta > 0 ? -3 * _lineHeight : 3 * _lineHeight;
            var newVal = Math.Clamp(_scrollBar.Value + delta, 0, _scrollBar.Maximum);
            _scrollBar.Value = newVal;
            _autoScroll = newVal >= _scrollBar.Maximum - 1;
            Invalidate();
        }

        private void UpdateScrollBar()
        {
            var viewHeight = Math.Max(1, Height);
            var totalPixels = Math.Max(0, _totalLines * _lineHeight - viewHeight);

            _scrollBar.Minimum = 0;
            _scrollBar.LargeChange = viewHeight;
            _scrollBar.SmallChange = _lineHeight;
            _scrollBar.Maximum = totalPixels;

            if (_autoScroll)
                _scrollBar.Value = _scrollBar.Maximum;
            else if (_scrollBar.Value > _scrollBar.Maximum)
                _scrollBar.Value = _scrollBar.Maximum;
        }

        private int HitTestLine(int y)
        {
            if (_totalLines <= 0)
                return 0;

            var lineIndex = (y + _scrollBar.Value) / _lineHeight;
            return Math.Clamp(lineIndex, 0, _totalLines - 1);
        }

        private void LogViewer_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            Focus();
            _selectStartLine = HitTestLine(e.Y);
            _selectEndLine = _selectStartLine;
            _selecting = true;
            Invalidate();
        }

        private void LogViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_selecting)
                return;

            _selectEndLine = HitTestLine(e.Y);
            Invalidate();
        }

        private void LogViewer_MouseUp(object sender, MouseEventArgs e)
        {
            _selecting = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);

            List<(LogItem log, int startLine, int lineCount)> snapshot;
            lock (_lock)
            {
                snapshot = _logLines.ToList();
            }

            var startPixel = _scrollBar.Value;
            var viewTopLine = startPixel / _lineHeight;
            var viewTopOffset = startPixel % _lineHeight;

            var selectionEnabled = _selectStartLine != -1 && _selectEndLine != -1;
            var selMin = Math.Min(_selectStartLine, _selectEndLine);
            var selMax = Math.Max(_selectStartLine, _selectEndLine);

            foreach (var (log, startLine, lineCount) in snapshot)
            {
                var endLine = startLine + lineCount - 1;
                if (endLine < viewTopLine)
                    continue;

                for (var i = 0; i < log.CachedLines.Length; i++)
                {
                    var currentLine = startLine + i;
                    if (currentLine < viewTopLine)
                        continue;

                    var y = (currentLine - viewTopLine) * _lineHeight - viewTopOffset;
                    if (y > Height)
                        return;

                    if (selectionEnabled && currentLine >= selMin && currentLine <= selMax)
                    {
                        e.Graphics.FillRectangle(Brushes.LightBlue, 0, y, Width - _scrollBar.Width, _lineHeight);
                    }

                    e.Graphics.DrawString(log.CachedLines[i], Font, GetBrush(log.Color), 4, y);
                }
            }
        }

        private void LogViewer_KeyDown(object sender, KeyEventArgs e)
        {
            if (!(e.Control && e.KeyCode == Keys.C))
                return;

            CopySelectedOrAll();
            e.Handled = true;
        }

        private void CopySelectedOrAll()
        {
            try
            {
                lock (_lock)
                {
                    var min = _selectStartLine;
                    var max = _selectEndLine;
                    if (min == -1 || max == -1)
                    {
                        Clipboard.SetText(GetAllLogs());
                        return;
                    }

                    if (min > max) (min, max) = (max, min);

                    var selectedText = new List<string>();
                    foreach (var (log, startLine, lineCount) in _logLines)
                    {
                        var endLine = startLine + lineCount - 1;
                        if (endLine < min || startLine > max)
                            continue;

                        var from = Math.Max(min - startLine, 0);
                        var to = Math.Min(max - startLine, log.CachedLines.Length - 1);

                        for (var li = from; li <= to; li++)
                            selectedText.Add(log.CachedLines[li]);
                    }

                    Clipboard.SetText(string.Join(Environment.NewLine, selectedText));
                }
            }
            catch
            {
            }
        }

        private Brush GetBrush(Color color)
        {
            if (_brushCache.TryGetValue(color, out var brush))
                return brush;

            var created = new SolidBrush(color);
            _brushCache[color] = created;
            return created;
        }

        private Color GetColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => Color.Gray,
                LogLevel.Debug => Color.DarkGray,
                LogLevel.Information => Color.Black,
                LogLevel.Warning => Color.Orange,
                LogLevel.Error => Color.Red,
                _ => Color.Black
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();

                foreach (var brush in _brushCache.Values)
                    brush.Dispose();
                _brushCache.Clear();
            }

            base.Dispose(disposing);
        }
    }
}
