using System.Windows;
using System.Windows.Threading;

namespace ApiKeyManager.Wpf.Services;

public sealed class ClipboardService : IDisposable
{
    private readonly ClipboardGuard _guard = new(() => Clipboard.ContainsText() ? Clipboard.GetText() : "", Clipboard.Clear);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public event Action? CleanupFailed;
    private bool _reported;
    public ClipboardService() { _timer.Tick += OnTick; _timer.Start(); }
    private void OnTick(object? sender, EventArgs e)
    {
        _guard.Tick(false);
        if (_guard.CleanupFailed && !_reported) { _reported = true; CleanupFailed?.Invoke(); }
    }
    public bool HasPending => _guard.HasPending;
    public TimeSpan? TimeLeft => _guard.TimeLeft;
    public bool SetText(string? value, int clearAfterSeconds)
    {
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, value), true);
            _guard.Track(value, clearAfterSeconds);
            _reported = false;
            return true;
        }
        catch { return false; }
    }
    public void Tick(bool force) => _guard.Tick(force);
    public void Dispose() { _guard.Tick(true); _timer.Stop(); _timer.Tick -= OnTick; }
}
