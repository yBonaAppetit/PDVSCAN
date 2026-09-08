using System.Text;

namespace PdvBarcodeFilter;

public sealed record CapturedScan(
    string RawCode,
    bool Overflowed,
    bool TimedOut = false,
    bool ReplaceCurrentField = false);

public sealed class ScanAccumulator
{
    private readonly int _maxLength;
    private readonly StringBuilder _buffer = new();
    private bool _overflowed;
    private long _lastActivityTick;

    public ScanAccumulator(int maxLength)
    {
        _maxLength = maxLength;
    }

    public void Append(string text)
    {
        if (_overflowed || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_buffer.Length + text.Length > _maxLength)
        {
            _buffer.Clear();
            _overflowed = true;
            _lastActivityTick = Environment.TickCount64;
            return;
        }

        _buffer.Append(text);
        _lastActivityTick = Environment.TickCount64;
    }

    public void Backspace()
    {
        if (!_overflowed && _buffer.Length > 0)
        {
            _buffer.Length--;
        }
    }

    public CapturedScan Complete()
    {
        var scan = new CapturedScan(_buffer.ToString(), _overflowed);
        Reset();
        return scan;
    }

    public void Reset()
    {
        _buffer.Clear();
        _overflowed = false;
        _lastActivityTick = 0;
    }

    public bool HasPendingInput => _buffer.Length > 0 || _overflowed;

    public int PendingLength => _buffer.Length;

    public bool IsTimedOut(int timeoutMs, long currentTick) =>
        HasPendingInput && _lastActivityTick > 0 && currentTick - _lastActivityTick >= timeoutMs;
}
