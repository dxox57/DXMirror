using DXMirror.Core.Receiver;
using DXMirror.Core.Settings;

namespace DXMirror.Core.Tests;

public class UxPlayStartupFileTests
{
    private static readonly ReceiverPaths Paths = new(@"C:\Data\key.pem", @"C:\Data\reg.txt", @"C:\Data\uxplay.rc");

    private static IReadOnlyList<string> Build(AppSettings s) =>
        UxPlayStartupFile.BuildOptionLines(s.Normalize(), Paths, UxPlayCapabilities.Assume174());

    [Fact]
    public void Default_settings_produce_expected_core_options()
    {
        var lines = Build(new AppSettings { DeviceName = "DXMirror – Mein PC" });
        Assert.Contains("n 'DXMirror – Mein PC'", lines);
        Assert.Contains("nh", lines);
        Assert.Contains("s 1920x1080@60", lines);
        Assert.Contains("fps 60", lines);
        Assert.Contains("vs d3d11videosink", lines);
        Assert.Contains("as wasapisink", lines);
        Assert.Contains("al 0.100", lines);
        Assert.Contains("vsync no", lines);
        Assert.Contains("reset 15", lines);
        Assert.Contains("nohold", lines);
        Assert.Contains(@"key C:\Data\key.pem", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("pin", StringComparison.Ordinal));
        Assert.DoesNotContain("avdec", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("d ", StringComparison.Ordinal));
    }

    [Fact]
    public void Audio_device_and_low_latency_are_passed_to_wasapisink()
    {
        var lines = Build(new AppSettings
        {
            AudioOutputDeviceId = "{0.0.0.00000000}.{98e35b2b-8eba-412e-b840-fd2c2492cf44}",
            WasapiLowLatency = true,
        });
        Assert.Contains("as 'wasapisink device=\"{0.0.0.00000000}.{98e35b2b-8eba-412e-b840-fd2c2492cf44}\" low-latency=true'", lines);
    }

    [Fact]
    public void Invalid_audio_device_id_is_ignored()
    {
        var lines = Build(new AppSettings { AudioOutputDeviceId = "x\" ! filesink location=evil" });
        Assert.Contains("as wasapisink", lines);
    }

    [Fact]
    public void Fixed_pin_uses_attached_syntax_and_register()
    {
        var lines = Build(new AppSettings { PinEnabled = true, FixedPin = "1234", RememberPinClients = true });
        Assert.Contains("pin1234", lines);
        Assert.Contains(@"reg C:\Data\reg.txt", lines);
    }

    [Fact]
    public void Random_pin_without_register()
    {
        var lines = Build(new AppSettings { PinEnabled = true, FixedPin = null, RememberPinClients = false });
        Assert.Contains("pin", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("reg", StringComparison.Ordinal));
    }

    [Fact]
    public void Audio_only_mode_disables_video()
    {
        var lines = Build(new AppSettings { AudioOnlyMode = true });
        Assert.Contains("vs 0", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("s ", StringComparison.Ordinal));
        Assert.DoesNotContain("vsync", lines);
    }

    [Fact]
    public void Software_decoding_stretch_d3d12_and_sync_offset()
    {
        var lines = Build(new AppSettings
        {
            HardwareAcceleration = false, VideoRenderer = "d3d12", Scaling = "stretch", AvSyncEnabled = true, AvSyncOffsetMs = -40,
        });
        Assert.Contains("avdec", lines);
        Assert.Contains("vs 'd3d12videosink force-aspect-ratio=false'", lines);
        Assert.Contains("vsync -40", lines);
    }

    [Fact]
    public void Debug_mode_enables_logging_options()
    {
        var lines = Build(new AppSettings { DebugMode = true });
        Assert.Contains("d 1", lines);
        Assert.Contains("FPSdata", lines);
    }

    [Fact]
    public void Unsupported_options_are_skipped()
    {
        var caps = UxPlayCapabilities.Parse("UxPlay 1.60\n-n name   x\n-nh   x\n-s wxh  x\n-vs ... x\n-as ... x\n-al x  x\n-reset n x\n-p   x\n-fs  x\n-d [n] x\n-h x\n", patched: false);
        var lines = UxPlayStartupFile.BuildOptionLines(new AppSettings().Normalize(), Paths, caps);
        Assert.DoesNotContain("nohold", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("key", StringComparison.Ordinal));
        Assert.Contains("nh", lines);
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("with space", "'with space'")]
    [InlineData("Hatm's PC", "\"Hatm's PC\"")]
    [InlineData("", "''")]
    [InlineData("#hash", "'#hash'")]
    public void Quote_handles_tokenizer_rules(string input, string expected)
    {
        Assert.Equal(expected, UxPlayStartupFile.Quote(input));
    }

    [Fact]
    public void File_content_is_one_option_per_line_with_header()
    {
        var content = UxPlayStartupFile.BuildFileContent(["n x", "nh"]);
        Assert.StartsWith("#", content);
        Assert.EndsWith("n x\nnh\n", content);
    }
}

public class UxPlayCapabilitiesTests
{
    private const string HelpExcerpt = """
        UxPlay 1.74: An open-source AirPlay mirroring server.
        =========== Website: https://github.com/FDH2/UxPlay ==========
        Usage: uxplay [-n name] [-s wxh] [-p [n]] [(other options)]
        Options:
        -n name   Specify network name of the AirPlay server (UTF-8/ascii)
        -nh       Do not add "@hostname" at the end of AirPlay server name
        -h265     Support h265 (4K) video (with h265 versions of h264 plugins)
        -pin[xxxx]Use a 4-digit pin code to control client access (default: no)
                  default pin is random: optionally use fixed pin xxxx
        -reg [fn] Keep a register in $HOME/.uxplay.register to verify returning
        -vsync [x]Mirror mode: sync audio to video using timestamps (default)
        -s wxh[@r]Request to client for video display resolution [refresh_rate]
        -fs       Full-screen (only with X11, Wayland, VAAPI, D3D11/12, kms)
        -p        Use legacy ports UDP 6000:6001:7011 TCP 7000:7001:7100
        -avdec    Force software h264 video decoding with libav decoder
        -vs ...   Choose the GSteamer videosink; default "autovideosink"
        -as ...   Choose the GStreamer audiosink; default "autoaudiosink"
        -al x     Audio latency in seconds (default 0.25) reported to client.
        -reset n  Reset after n seconds of client silence (default n=15, 0=never)
        -nohold   Drop current connection when new client connects.
        -FPSdata  Show video-streaming performance reports sent by client.
        -fps n    Set maximum allowed streaming framerate, default 30
        -key [fn] Store private key in $HOME/.uxplay.pem (or in file "fn")
        -d [n]    Enable debug logging; optional: n=1 to skip normal packet data
        """;

    [Fact]
    public void Parses_options_and_version_from_help()
    {
        var caps = UxPlayCapabilities.Parse(HelpExcerpt, patched: true);
        Assert.Equal("1.74", caps.Version);
        foreach (var o in new[] { "n", "nh", "h265", "pin", "reg", "vsync", "s", "fs", "p", "avdec", "vs", "as", "al", "reset", "nohold", "FPSdata", "fps", "key", "d" })
            Assert.True(caps.Supports(o), o);
        Assert.False(caps.Supports("doesnotexist"));
    }

    [Fact]
    public void Garbage_help_falls_back_to_known_option_set()
    {
        var caps = UxPlayCapabilities.Parse("segfault", patched: false);
        Assert.True(caps.Supports("nohold"));
        Assert.False(caps.Supports("doesnotexist"));
    }
}

public class SettingsTests
{
    [Fact]
    public void Normalize_clamps_out_of_range_values()
    {
        var s = new AppSettings
        {
            MaxWidth = 99999, MaxHeight = 1, MaxFps = 144, Volume = double.NaN, AudioLatencyMs = 1,
            AvSyncOffsetMs = 99999, ConnectionTimeoutSeconds = -5, Theme = "neon", Language = "fr",
            FixedPin = "12a4", VideoRenderer = "opengl", Scaling = "zoom", LogLevel = "trace",
        }.Normalize();
        Assert.Equal(3840, s.MaxWidth);
        Assert.Equal(240, s.MaxHeight);
        Assert.Equal(60, s.MaxFps);
        Assert.Equal(1.0, s.Volume);
        Assert.Equal(50, s.AudioLatencyMs);
        Assert.Equal(1000, s.AvSyncOffsetMs);
        Assert.Equal(0, s.ConnectionTimeoutSeconds);
        Assert.Equal("system", s.Theme);
        Assert.Equal("auto", s.Language);
        Assert.Null(s.FixedPin);
        Assert.Equal("d3d11", s.VideoRenderer);
        Assert.Equal("fit", s.Scaling);
        Assert.Equal("info", s.LogLevel);
    }

    [Theory]
    [InlineData("DXMirror – Mein PC", "DXMirror – Mein PC")]
    [InlineData("  Wohnzimmer   PC  ", "Wohnzimmer PC")]
    [InlineData("bad\"name\\@x.y", "badnamexy")]
    [InlineData("tab\tchar", "tabchar")]
    public void Device_name_is_sanitised(string input, string expected)
    {
        Assert.Equal(expected, DeviceNameRules.Sanitize(input));
    }

    [Fact]
    public void Device_name_is_capped_in_utf8_bytes()
    {
        var name = DeviceNameRules.Sanitize(new string('ä', 50));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name) <= 60);
    }

    [Fact]
    public void Empty_device_name_gets_default()
    {
        Assert.StartsWith("DXMirror", DeviceNameRules.Sanitize("   "));
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("0000", true)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("12a4", false)]
    [InlineData(null, false)]
    public void Pin_rules(string? pin, bool valid) => Assert.Equal(valid, PinRules.IsValid(pin));

    [Fact]
    public void Store_round_trips_and_protects_pin()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(dir.FullName, "settings.json");
            var store = new SettingsStore(file, p => "ENC" + new string(p.Reverse().ToArray()), c => c.StartsWith("ENC") ? new string(c[3..].Reverse().ToArray()) : null);
            store.Save(new AppSettings { DeviceName = "Test", FixedPin = "4321", PinEnabled = true, Volume = 0.4 });
            var raw = File.ReadAllText(file);
            Assert.DoesNotContain("4321", raw);
            var loaded = store.Load();
            Assert.Equal("Test", loaded.DeviceName);
            Assert.Equal("4321", loaded.FixedPin);
            Assert.Equal(0.4, loaded.Volume, 3);
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Corrupt_file_is_quarantined_and_defaults_used()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(file, "{ this is not json");
            var loaded = new SettingsStore(file).Load();
            Assert.Equal(new AppSettings().Normalize().MaxFps, loaded.MaxFps);
            Assert.True(File.Exists(file + ".corrupt"));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var loaded = new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")).Load();
        Assert.True(loaded.StartReceiverOnLaunch);
        Assert.Equal(1920, loaded.MaxWidth);
    }
}
