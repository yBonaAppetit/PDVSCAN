namespace PdvBarcodeFilter;

public enum FloodDecision
{
    Allowed,
    JustBlocked,
    Blocked
}

public sealed class ScannerFloodGuard
{
    private readonly int _windowMs;
    private readonly int _maxScans;
    private readonly int _cooldownMs;
    private readonly Queue<long> _recentScans = new();
    private long _blockedUntil;

    public ScannerFloodGuard(FilterSettings settings)
    {
        _windowMs = settings.FloodWindowMs;
        _maxScans = settings.MaxScansPerFloodWindow;
        _cooldownMs = settings.FloodCooldownMs;
    }

    public FloodDecision Evaluate(long currentTick)
    {
        if (currentTick < _blockedUntil)
        {
            return FloodDecision.Blocked;
        }

        while (_recentScans.Count > 0 && currentTick - _recentScans.Peek() > _windowMs)
        {
            _recentScans.Dequeue();
        }

        _recentScans.Enqueue(currentTick);
        if (_recentScans.Count <= _maxScans)
        {
            return FloodDecision.Allowed;
        }

        _recentScans.Clear();
        _blockedUntil = currentTick + _cooldownMs;
        return FloodDecision.JustBlocked;
    }
}
