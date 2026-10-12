namespace QTP.Common;

/// <summary>Drain stderr without blocking the browser or retaining unbounded output.</summary>
internal sealed class ChromiumDiagnosticBuffer
{
    private readonly object _sync = new();
    private readonly Queue<string> _lines = new();
    public void Add(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_sync)
        {
            _lines.Enqueue(line.Length > 1000 ? line[..1000] : line);
            while (_lines.Count > 32) _lines.Dequeue();
        }
    }
    public string Text { get { lock (_sync) return string.Join(Environment.NewLine, _lines); } }
}
