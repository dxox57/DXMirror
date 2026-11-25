using DXMirror.Core.Receiver;

namespace DXMirror.Core.Tests;

public class UxPlayEventParserTests
{
    [Fact]
    public void Parses_name_and_values()
    {
        var e = UxPlayEventParser.TryParse("@@DXM video_size codec=h264 src=1920x1080 w=1170 h=2532");
        Assert.NotNull(e);
        Assert.Equal("video_size", e!.Name);
        Assert.Equal("h264", e.Get("codec"));
        Assert.Equal(1170, e.GetInt("w"));
        Assert.Equal(2532, e.GetInt("h"));
        Assert.Equal("1920x1080", e.Get("src"));
    }

    [Fact]
    public void Parses_quoted_values_with_spaces()
    {
        var e = UxPlayEventParser.TryParse("@@DXM client_request name=\"Annas iPhone\" model=\"iPhone15,2\"");
        Assert.Equal("Annas iPhone", e!.Get("name"));
        Assert.Equal("iPhone15,2", e.Get("model"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("normal uxplay log line")]
    [InlineData("@@DXM ")]
    [InlineData("@@DXM bad-name x=1")]
    [InlineData("@@DXM stats fps")]
    [InlineData("@@DXM client_request name=\"unterminated")]
    [InlineData("@@DXM stats =5")]
    public void Rejects_non_events_and_malformed_lines(string? line)
    {
        Assert.Null(UxPlayEventParser.TryParse(line));
    }

    [Fact]
    public void Rejects_oversized_lines()
    {
        Assert.Null(UxPlayEventParser.TryParse("@@DXM stats fps=1 x=" + new string('a', 5000)));
    }

    [Fact]
    public void Non_numeric_values_return_null_numbers()
    {
        var e = UxPlayEventParser.TryParse("@@DXM stats fps=abc kbps=12");
        Assert.Null(e!.GetInt("fps"));
        Assert.Equal(12, e.GetLong("kbps"));
    }
}

public class ReceiverSessionTests
{
    private static UxPlayEvent Ev(string line) => UxPlayEventParser.TryParse(line) ?? throw new ArgumentException(line);

    [Fact]
    public void Full_mirroring_session_lifecycle()
    {
        var s = new ReceiverSession();
        s.SetState(ReceiverState.Starting);
        Assert.Equal(ReceiverState.Ready, s.Apply(Ev("@@DXM started version=1.74")).State);
        Assert.Equal("1.74", s.Snapshot.ReceiverVersion);

        var c = s.Apply(Ev("@@DXM client_request name=\"Test iPhone\" model=\"iPhone16,1\""));
        Assert.Equal(ReceiverState.Connecting, c.State);
        Assert.Equal("Test iPhone", c.ClientName);
        Assert.NotNull(c.ConnectedSince);

        s.Apply(Ev("@@DXM conn_open count=1"));
        var v = s.Apply(Ev("@@DXM video_size codec=h264 src=1920x1080 w=886 h=1920"));
        Assert.Equal(ReceiverState.Streaming, v.State);
        Assert.Equal(StreamKind.ScreenMirroring, v.Kind);
        Assert.False(v.IsLandscape);

        var a = s.Apply(Ev("@@DXM audio_format ct=8 spf=480 screen=1 media=0"));
        Assert.True(a.AudioActive);
        Assert.Equal("AAC-ELD", a.AudioFormat);
        Assert.Equal(StreamKind.ScreenMirroring, a.Kind);

        var st = s.Apply(Ev("@@DXM stats fps=58 kbps=6400 missed=0"));
        Assert.Equal(58, st.Fps);
        Assert.Equal(6400, st.VideoKbps);

        var rotated = s.Apply(Ev("@@DXM video_size codec=h264 src=1920x1080 w=1920 h=886"));
        Assert.True(rotated.IsLandscape);

        var closed = s.Apply(Ev("@@DXM conn_close count=0"));
        Assert.Equal(ReceiverState.Ready, closed.State);
        Assert.Null(closed.ClientName);
        Assert.False(closed.AudioActive);
        Assert.Equal(StreamKind.None, closed.Kind);
    }

    [Fact]
    public void Audio_only_session_is_detected()
    {
        var s = new ReceiverSession();
        s.Apply(Ev("@@DXM started version=1.74"));
        s.Apply(Ev("@@DXM conn_open count=1"));
        var a = s.Apply(Ev("@@DXM audio_format ct=2 spf=352 screen=0 media=1"));
        Assert.Equal(StreamKind.AudioOnly, a.Kind);
        Assert.Equal("ALAC", a.AudioFormat);
        Assert.Equal(ReceiverState.Streaming, a.State);
    }

    [Fact]
    public void Connection_reset_returns_to_ready_with_reason()
    {
        var s = new ReceiverSession();
        s.Apply(Ev("@@DXM started version=1.74"));
        s.Apply(Ev("@@DXM client_request name=\"x\" model=\"y\""));
        s.Apply(Ev("@@DXM video_size codec=h264 src=1x1 w=100 h=200"));
        var r = s.Apply(Ev("@@DXM conn_reset reason=1"));
        Assert.Equal(ReceiverState.Ready, r.State);
        Assert.Equal("connection_lost", r.LastError);
        Assert.Null(r.VideoWidth);
    }

    [Fact]
    public void Pin_is_shown_until_stream_starts()
    {
        var s = new ReceiverSession();
        s.Apply(Ev("@@DXM started version=1.74"));
        s.Apply(Ev("@@DXM client_request name=\"x\" model=\"y\""));
        Assert.Equal("4711", s.Apply(Ev("@@DXM pin code=4711")).PendingPin);
        Assert.Null(s.Apply(Ev("@@DXM video_size codec=h264 src=1x1 w=10 h=20")).PendingPin);
    }

    [Fact]
    public void Denied_client_is_cleared()
    {
        var s = new ReceiverSession();
        s.Apply(Ev("@@DXM started version=1.74"));
        s.Apply(Ev("@@DXM client_request name=\"x\" model=\"y\""));
        var d = s.Apply(Ev("@@DXM client_denied"));
        Assert.Equal(ReceiverState.Ready, d.State);
        Assert.Null(d.ClientName);
    }

    [Fact]
    public void Absurd_video_sizes_are_ignored()
    {
        var s = new ReceiverSession();
        var v = s.Apply(Ev("@@DXM video_size codec=h264 src=1x1 w=0 h=99999"));
        Assert.Null(v.VideoWidth);
        Assert.Null(v.VideoHeight);
    }

    [Fact]
    public void Recording_states_follow_receiver_events()
    {
        var s = new ReceiverSession();
        s.Apply(Ev("@@DXM started version=1.74"));
        Assert.Equal(RecordingState.Armed, s.Apply(Ev("@@DXM recording state=armed")).Recording);
        Assert.Equal(RecordingState.Waiting, s.Apply(Ev("@@DXM recording state=waiting")).Recording);
        Assert.Equal(RecordingState.Recording, s.Apply(Ev("@@DXM recording state=started")).Recording);
        Assert.Equal(RecordingState.Off, s.Apply(Ev("@@DXM recording state=stopped")).Recording);
        Assert.Equal(RecordingState.Failed, s.Apply(Ev("@@DXM recording state=error")).Recording);
        s.Apply(Ev("@@DXM recording state=started"));
        Assert.Equal(RecordingState.Off, s.SetState(ReceiverState.Stopped).Recording);
    }

    [Fact]
    public void Stopping_clears_client_state()
    {
        var s = new ReceiverSession();
        s.Apply(Ev("@@DXM client_request name=\"x\" model=\"y\""));
        var stopped = s.SetState(ReceiverState.Stopped);
        Assert.Null(stopped.ClientName);
        Assert.False(stopped.HasClient);
    }
}
