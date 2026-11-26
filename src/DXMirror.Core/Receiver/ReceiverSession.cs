namespace DXMirror.Core.Receiver;

public enum ReceiverState
{
    Stopped,
    Starting,
    /// <summary>Receiver is running and advertised via mDNS; waiting for an iPhone.</summary>
    Ready,
    /// <summary>An iPhone opened a connection (pairing / setup).</summary>
    Connecting,
    /// <summary>Video and/or audio is being received.</summary>
    Streaming,
    Stopping,
    /// <summary>The receiver process failed; see <see cref="ReceiverSnapshot.LastError"/>.</summary>
    Faulted,
}

public enum RecordingState
{
    Off,
    /// <summary>Recording requested, no stream yet: starts with the next connection.</summary>
    Armed,
    /// <summary>Stream running, waiting for the next key frame from the iPhone.</summary>
    Waiting,
    Recording,
    /// <summary>The receiver could not create the recording pipeline.</summary>
    Failed,
}

public enum StreamKind
{
    None,
    ScreenMirroring,
    AudioOnly,
}

/// <summary>Immutable view of the receiver for the UI (safe to pass between threads).</summary>
public sealed record ReceiverSnapshot
{
    public ReceiverState State { get; init; } = ReceiverState.Stopped;
    public string? ClientName { get; init; }
    public string? ClientModel { get; init; }
    public DateTimeOffset? ConnectedSince { get; init; }
    public int? VideoWidth { get; init; }
    public int? VideoHeight { get; init; }
    public string? VideoCodec { get; init; }
    public double Fps { get; init; }
    public long VideoKbps { get; init; }
    public bool AudioActive { get; init; }
    /// <summary>"AAC-ELD" (mirroring), "ALAC" or "AAC" (music streaming) when known.</summary>
    public string? AudioFormat { get; init; }
    public StreamKind Kind { get; init; }
    public int MissedHeartbeats { get; init; }
    public string? LastError { get; init; }
    public string? ReceiverVersion { get; init; }
    public bool StatusEventsAvailable { get; init; }
    public RecordingState Recording { get; init; }

    public bool IsLandscape => VideoWidth is { } w && VideoHeight is { } h && w >= h;
    public bool HasClient => State is ReceiverState.Connecting or ReceiverState.Streaming;
}

/// <summary>
/// Pure state machine that turns <see cref="UxPlayEvent"/>s into <see cref="ReceiverSnapshot"/>s.
/// No I/O, no threads – fully unit-testable. Callers must serialise access (the controller does).
/// </summary>
public sealed class ReceiverSession
{
    private readonly TimeProvider _time;
    private ReceiverSnapshot _snapshot = new();
    private int _openConnections;

    public ReceiverSession(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public ReceiverSnapshot Snapshot => _snapshot;

    public ReceiverSnapshot SetState(ReceiverState state, string? error = null)
    {
        _snapshot = state switch
        {
            ReceiverState.Stopped or ReceiverState.Faulted or ReceiverState.Starting => ClearClient(_snapshot) with { State = state, Recording = RecordingState.Off, LastError = error ?? (state == ReceiverState.Faulted ? _snapshot.LastError : null) },
            _ => _snapshot with { State = state, LastError = error ?? _snapshot.LastError },
        };
        if (state is ReceiverState.Stopped or ReceiverState.Faulted or ReceiverState.Starting)
        {
            _openConnections = 0;
        }
        return _snapshot;
    }

    public ReceiverSnapshot Apply(UxPlayEvent e)
    {
        var s = _snapshot with { StatusEventsAvailable = true };
        switch (e.Name)
        {
            case "started":
                s = s with { State = ReceiverState.Ready, ReceiverVersion = e.Get("version"), LastError = null };
                break;

            case "client_request":
                s = s with
                {
                    State = ReceiverState.Connecting,
                    ClientName = NonEmpty(e.Get("name")) ?? s.ClientName,
                    ClientModel = NonEmpty(e.Get("model")) ?? s.ClientModel,
                    ConnectedSince = s.ConnectedSince ?? _time.GetUtcNow(),
                };
                break;

            case "client_denied":
                s = ClearClient(s) with { State = ReceiverState.Ready };
                break;

            case "conn_open":
                _openConnections = e.GetInt("count") ?? _openConnections + 1;
                if (s.State is ReceiverState.Ready) s = s with { State = ReceiverState.Connecting, ConnectedSince = _time.GetUtcNow() };
                break;

            case "conn_close":
                _openConnections = Math.Max(0, e.GetInt("count") ?? _openConnections - 1);
                if (_openConnections == 0) s = ClearClient(s) with { State = ReceiverState.Ready };
                break;

            case "conn_reset":
                // UxPlay restarts its internal loop; the session is over from the user's point of view
                _openConnections = 0;
                s = ClearClient(s) with
                {
                    State = ReceiverState.Ready,
                    LastError = e.GetInt("reason") == 1 ? "connection_lost" : s.LastError,
                };
                break;

            case "video_size":
                s = s with
                {
                    State = ReceiverState.Streaming,
                    Kind = StreamKind.ScreenMirroring,
                    VideoWidth = PositiveOrNull(e.GetInt("w")),
                    VideoHeight = PositiveOrNull(e.GetInt("h")),
                    VideoCodec = e.Get("codec"),
                    ConnectedSince = s.ConnectedSince ?? _time.GetUtcNow(),
                };
                break;

            case "audio_format":
                var ct = e.GetInt("ct");
                bool screen = e.GetInt("screen") == 1;
                s = s with
                {
                    AudioActive = true,
                    AudioFormat = ct switch { 2 => "ALAC", 4 => "AAC", 8 => "AAC-ELD", _ => s.AudioFormat },
                    Kind = s.Kind == StreamKind.ScreenMirroring || screen ? StreamKind.ScreenMirroring : StreamKind.AudioOnly,
                    State = ReceiverState.Streaming,
                    ConnectedSince = s.ConnectedSince ?? _time.GetUtcNow(),
                };
                break;

            case "stats":
                s = s with
                {
                    Fps = Math.Max(0, e.GetInt("fps") ?? 0),
                    VideoKbps = Math.Max(0, e.GetLong("kbps") ?? 0),
                    MissedHeartbeats = Math.Max(0, e.GetInt("missed") ?? 0),
                };
                break;

            case "recording":
                s = s with
                {
                    Recording = e.Get("state") switch
                    {
                        "started" => RecordingState.Recording,
                        "waiting" => RecordingState.Waiting,
                        "armed" => RecordingState.Armed,
                        "error" => RecordingState.Failed,
                        _ => RecordingState.Off,
                    },
                };
                break;

            case "stopping":
            case "quitting":
                s = s with { State = ReceiverState.Stopping };
                break;
        }
        _snapshot = s;
        return s;
    }

    private static ReceiverSnapshot ClearClient(ReceiverSnapshot s) => s with
    {
        ClientName = null,
        ClientModel = null,
        ConnectedSince = null,
        VideoWidth = null,
        VideoHeight = null,
        VideoCodec = null,
        Fps = 0,
        VideoKbps = 0,
        AudioActive = false,
        AudioFormat = null,
        Kind = StreamKind.None,
        MissedHeartbeats = 0,
    };

    private static string? NonEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v;
    private static int? PositiveOrNull(int? v) => v is > 0 and < 10000 ? v : null;
}
