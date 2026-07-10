namespace ServerCenter.Core.Connection;

// Reconnect backoff for the agent's dial loop. Full jitter over an exponentially growing cap:
// the delay for attempt n is uniformly random in [0, min(max, base * 2^n)]. Full jitter avoids
// thundering-herd reconnects when the controller comes back. The rng is injectable so the
// delay is deterministic in tests.
public sealed class BackoffPolicy
{
    private readonly TimeSpan _base;
    private readonly TimeSpan _max;
    private readonly Func<double> _rng;

    public BackoffPolicy(TimeSpan baseDelay, TimeSpan maxDelay, Func<double>? rng = null)
    {
        if (baseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay), "must be positive");
        }

        if (maxDelay < baseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "must be >= baseDelay");
        }

        _base = baseDelay;
        _max = maxDelay;
        _rng = rng ?? Random.Shared.NextDouble;
    }

    public TimeSpan NextDelay(int attempt)
    {
        if (attempt < 0)
        {
            attempt = 0;
        }

        double cap = Math.Min(_max.TotalMilliseconds, _base.TotalMilliseconds * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(cap * _rng());
    }
}
