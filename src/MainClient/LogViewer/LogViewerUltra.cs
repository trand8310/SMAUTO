using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Timer = System.Windows.Forms.Timer;


namespace MainClient.LogViewer
{

    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Windows.Forms;

 

    public class LogItem
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
        public Color Color { get; set; }
    }

    public class LogViewerUltra : Control
    {
        private readonly List<LogItem> _logs = new();
        private readonly List<LogItem> _filteredLogs = new();
        private readonly ConcurrentQueue<LogItem> _queue = new();
        private readonly object _lock = new();

        private readonly VScrollBar _scrollBar = new();
        private readonly Timer _timer = new();

        private int _lineHeight = 18;
        private bool _autoScroll = true;
        private bool _paused = false;
        private string _filter = "";

        // 选中
        private int _selectStartLine = -1;
        private int _selectEndLine = -1;
        private bool _selecting = false;

        // 每条日志拆分的行信息，用于选择和滚动
        private readonly List<(LogItem log, int startLine, int lineCount)> _logLines = new();

        public int MaxLogs { get; set; } = 1_000_000;

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

            _timer.Interval = 100;
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

        #region Public API

        public void WriteLog(string message, LogLevel level)
        {
            var log = new LogItem
            {
                Time = DateTime.Now,
                Level = level,
                Message = message,
                Color = GetColor(level)
            };
            _queue.Enqueue(log);
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
            }
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
            _filter = text ?? "";
            ApplyFilter();
            UpdateScrollBar();
            Invalidate();
        }

        #endregion

        #region Queue Flush

        private void FlushQueue()
        {
            if (_paused) return;

            int count = 0;
            while (_queue.TryDequeue(out var log))
            {
                lock (_lock)
                {
                    _logs.Add(log);
                    if (_logs.Count > MaxLogs)
                    {
                        int remove = _logs.Count - MaxLogs;
                        _logs.RemoveRange(0, remove);
                    }
                }
                count++;
                if (count > 5000) break;
            }

            ApplyFilter();
            UpdateScrollBar();

            if (_autoScroll)
                _scrollBar.Value = Math.Max(0, _scrollBar.Maximum);

            Invalidate();
        }

        private void ApplyFilter()
        {
            lock (_lock)
            {
                _filteredLogs.Clear();
                if (string.IsNullOrEmpty(_filter))
                    _filteredLogs.AddRange(_logs);
                else
                    _filteredLogs.AddRange(_logs.Where(x => x.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase)));

                // 重新计算每条日志的行信息
                _logLines.Clear();
                int lineCounter = 0;
                foreach (var log in _filteredLogs)
                {
                    int lineCount = log.Message.Count(c => c == '\n') + 1;
                    _logLines.Add((log, lineCounter, lineCount));
                    lineCounter += lineCount;
                }
            }
        }

        #endregion

        #region Scroll

        private void LogViewer_MouseWheel(object sender, MouseEventArgs e)
        {
            int delta = e.Delta > 0 ? -3 * _lineHeight : 3 * _lineHeight;
            int newVal = Math.Clamp(_scrollBar.Value + delta, 0, _scrollBar.Maximum);
            _scrollBar.Value = newVal;
            _autoScroll = newVal >= _scrollBar.Maximum - 1;
            Invalidate();
        }

        private void UpdateScrollBar()
        {
            int totalLines = _logLines.Count == 0 ? 0 : _logLines.Last().startLine + _logLines.Last().lineCount;
            _scrollBar.Maximum = Math.Max(0, totalLines * _lineHeight - Height);
            _scrollBar.LargeChange = Height;
            if (_autoScroll)
                _scrollBar.Value = _scrollBar.Maximum;
        }

        private int HitTestLine(int y)
        {
            int lineIndex = (y + _scrollBar.Value) / _lineHeight;
            return Math.Clamp(lineIndex, 0, _logLines.LastOrDefault().startLine + _logLines.LastOrDefault().lineCount - 1);
        }

        #endregion

        #region Mouse Selection

        private void LogViewer_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                _selectStartLine = HitTestLine(e.Y);
                _selectEndLine = _selectStartLine;
                _selecting = true;
                Invalidate();
            }
        }

        private void LogViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_selecting)
            {
                _selectEndLine = HitTestLine(e.Y);
                Invalidate();
            }
        }

        private void LogViewer_MouseUp(object sender, MouseEventArgs e)
        {
            _selecting = false;
        }

        #endregion

        #region Painting

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);

            int offsetY = _scrollBar.Value; // 滚动像素
            int y = -offsetY;               // 起始绘制位置

            lock (_lock)
            {
                foreach (var (log, startLine, lineCount) in _logLines)
                {
                    string[] lines = $"{log.Time:HH:mm:ss} [{log.Level}] {log.Message}".Split('\n');

                    for (int li = 0; li < lines.Length; li++)
                    {
                        int lineIndex = startLine + li;

                        // 如果绘制位置超出可见区域就跳过
                        if (y + _lineHeight < 0)
                        {
                            y += _lineHeight;
                            continue;
                        }

                        if (y > Height) break;

                        // 绘制选中背景
                        if (_selectStartLine != -1 && _selectEndLine != -1)
                        {
                            int min = Math.Min(_selectStartLine, _selectEndLine);
                            int max = Math.Max(_selectStartLine, _selectEndLine);
                            if (lineIndex >= min && lineIndex <= max)
                            {
                                using var brush = new SolidBrush(Color.LightBlue);
                                e.Graphics.FillRectangle(brush, 0, y, Width - _scrollBar.Width, _lineHeight);
                            }
                        }

                        // 绘制文本
                        using var brushColor = new SolidBrush(log.Color);
                        e.Graphics.DrawString(lines[li], Font, brushColor, 4, y);

                        y += _lineHeight;
                    }

                    // 如果当前绘制位置已经超出控件高度，直接结束绘制
                    if (y > Height) break;
                }
            }
        }


        #endregion

        #region Keyboard

        private void LogViewer_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedOrAll();
                e.Handled = true;
            }
        }

        private void CopySelectedOrAll()
        {
            lock (_lock)
            {
                int min = _selectStartLine;
                int max = _selectEndLine;
                if (min == -1 || max == -1)
                {
                    Clipboard.SetText(GetAllLogs());
                    return;
                }

                if (min > max) (min, max) = (max, min);

                var selectedText = new List<string>();
                foreach (var (log, startLine, lineCount) in _logLines)
                {
                    int endLine = startLine + lineCount - 1;
                    if (endLine < min || startLine > max) continue;

                    var lines = $"{log.Time:HH:mm:ss} [{log.Level}] {log.Message}".Split('\n');
                    int from = Math.Max(min - startLine, 0);
                    int to = Math.Min(max - startLine, lines.Length - 1);

                    for (int li = from; li <= to; li++)
                        selectedText.Add(lines[li]);
                }

                Clipboard.SetText(string.Join(Environment.NewLine, selectedText));
            }
        }

        #endregion

        #region Color Mapping

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

        #endregion
    }

}