namespace DXMirror.Core.Receiver;

/// <summary>
/// Exponential back-off for automatic receiver restarts. A run that stayed up for at least
/// <see cref="StableAfter"/> resets the counter; after <see cref="MaxAttemptsInRow"/> quick
/// failures in a row the controller gives up and reports a fault (prevents a crash loop).
/// </summary>
public sealed class RestartPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
    ];

    public TimeSpan StableAfter { get; init; } = TimeSpan.FromSeconds(60);
    public int MaxAttemptsInRow { get; init; } = 6;
    public int ConsecutiveFailures { get; private set; }

    /// <summary>Returns the delay before the next restart, or null if the controller should give up.</summary>
    public TimeSpan? OnUnexpectedExit(TimeSpan uptime)
    {
        if (uptime >= StableAfter) ConsecutiveFailures = 0;
        ConsecutiveFailures++;
        if (ConsecutiveFailures > MaxAttemptsInRow) return null;
        return Delays[Math.Min(ConsecutiveFailures - 1, Delays.Length - 1)];
    }

    public void Reset() => ConsecutiveFailures = 0;
}
