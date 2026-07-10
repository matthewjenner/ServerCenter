namespace ServerCenter.Core.Connection;

// Agent-online is one of the two independent truths (brief 3.7). It is derived from heartbeat
// gaps: healthy -> Stale -> Offline as the gap grows. A dropped stream is immediate Offline
// and is handled at the connection layer, not here. Pure: caller supplies 'now', so it is
// testable without a clock.
public sealed class LivenessTracker
{
    private readonly long _staleAfterMs;
    private readonly long _offlineAfterMs;

    public LivenessTracker(long staleAfterMs, long offlineAfterMs)
    {
        if (staleAfterMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfterMs), "must be positive");
        }

        if (offlineAfterMs <= staleAfterMs)
        {
            throw new ArgumentOutOfRangeException(nameof(offlineAfterMs), "must exceed staleAfterMs");
        }

        _staleAfterMs = staleAfterMs;
        _offlineAfterMs = offlineAfterMs;
    }

    public AgentLiveness Evaluate(long lastHeartbeatUnixMs, long nowUnixMs)
    {
        long gap = nowUnixMs - lastHeartbeatUnixMs;
        if (gap >= _offlineAfterMs)
        {
            return AgentLiveness.Offline;
        }

        return gap >= _staleAfterMs ? AgentLiveness.Stale : AgentLiveness.Online;
    }
}

public enum AgentLiveness
{
    Online,
    Stale,
    Offline
}
