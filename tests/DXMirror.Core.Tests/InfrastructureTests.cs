using System.IO.Compression;
using System.Net.NetworkInformation;
using DXMirror.Core.Diagnostics;
using DXMirror.Core.Network;
using DXMirror.Core.Receiver;
using DXMirror.Core.Settings;

namespace DXMirror.Core.Tests;

public class LogRedactorTests
{
    [Theory]
    [InlineData("@@DXM pin code=1234", "@@DXM pin code=[redacted]")]
    [InlineData("client entered pin 4711 ok", "client entered pin [redacted] ok")]
    [InlineData("password=geheim123 rest", "password=[redacted] rest")]
    [InlineData("pk 0123456789abcdef0123456789abcdef0123", "pk [hex redacted]")]
    public void Secrets_are_removed(string input, string expected) => Assert.Equal(expected, LogRedactor.Redact(input));

    [Fact]
    public void Normal_lines_are_untouched()
    {
        const string line = "Accepted IPv4 client on socket 1234, video 1920x1080";
        Assert.Equal(line, LogRedactor.Redact(line));
    }
}

public class RestartPolicyTests
{
    [Fact]
    public void Backs_off_and_gives_up()
    {
        var p = new RestartPolicy { MaxAttemptsInRow = 3 };
        Assert.Equal(TimeSpan.FromSeconds(1), p.OnUnexpectedExit(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(2), p.OnUnexpectedExit(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(5), p.OnUnexpectedExit(TimeSpan.FromSeconds(1)));
        Assert.Null(p.OnUnexpectedExit(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Stable_run_resets_counter()
    {
        var p = new RestartPolicy();
        p.OnUnexpectedExit(TimeSpan.Zero);
        p.OnUnexpectedExit(TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(1), p.OnUnexpectedExit(TimeSpan.FromMinutes(10)));
        Assert.Equal(1, p.ConsecutiveFailures);
    }
}

public class NetworkAdapterTests
{
    private static NetworkAdapterInfo A(string id, AdapterKind kind, bool up = true, bool gw = true, string? ip = "192.168.1.20") =>
        new(id, id, id, kind, up, gw, ip is null ? [] : [ip], [], 0);

    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, "WLAN", "Intel Wi-Fi 6E AX211", AdapterKind.WiFi)]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet", "Realtek PCIe GbE", AdapterKind.Ethernet)]
    [InlineData(NetworkInterfaceType.Ethernet, "vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", AdapterKind.Virtual)]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet 3", "WireGuard Tunnel", AdapterKind.Vpn)]
    [InlineData(NetworkInterfaceType.Unknown, "OpenVPN TAP", "TAP-Windows Adapter V9", AdapterKind.Vpn)]
    public void Classifies_adapters(NetworkInterfaceType type, string name, string desc, AdapterKind expected) =>
        Assert.Equal(expected, NetworkAdapterService.Classify(type, name, desc));

    [Fact]
    public void Automatic_selection_prefers_physical_adapters()
    {
        var list = new[] { A("vpn", AdapterKind.Vpn), A("wifi", AdapterKind.WiFi), A("hyperv", AdapterKind.Virtual), A("eth", AdapterKind.Ethernet, gw: false) };
        Assert.Equal("wifi", NetworkAdapterService.Select(list, automatic: true, preferredId: null)!.Id);
    }

    [Fact]
    public void Manual_selection_is_used_and_falls_back_when_gone()
    {
        var list = new[] { A("wifi", AdapterKind.WiFi), A("eth", AdapterKind.Ethernet) };
        Assert.Equal("wifi", NetworkAdapterService.Select(list, automatic: false, preferredId: "wifi")!.Id);
        Assert.Equal("eth", NetworkAdapterService.Select(list, automatic: false, preferredId: "missing")!.Id);
    }

    [Fact]
    public void Down_or_unaddressed_adapters_are_never_selected()
    {
        var list = new[] { A("down", AdapterKind.Ethernet, up: false), A("noip", AdapterKind.WiFi, ip: null) };
        Assert.Null(NetworkAdapterService.Select(list, automatic: true, preferredId: null));
    }

    [Theory]
    [InlineData("192.168.0.5", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("fe80::1", false)]
    [InlineData("garbage", false)]
    public void Private_ipv4_detection(string ip, bool expected) => Assert.Equal(expected, NetworkAdapterService.IsPrivateIPv4(ip));
}

public class AppLogAndReportTests
{
    [Fact]
    public async Task Writes_redacted_entries_to_memory_and_file()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var log = new AppLog(dir.FullName);
            log.Info("Test", "hello world");
            log.Info("Test", "@@DXM pin code=9876");
            log.Debug("Test", "hidden at info level");
            await log.DisposeAsync();
            var text = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "dxmirror.log"));
            Assert.Contains("hello world", text);
            Assert.DoesNotContain("9876", text);
            Assert.DoesNotContain("hidden at info level", text);
            Assert.Equal(2, log.Recent().Count);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Memory_buffer_is_bounded()
    {
        var log = new AppLog(null);
        for (int i = 0; i < AppLog.MemoryCapacity + 500; i++) log.Info("T", "line " + i);
        Assert.True(log.Recent().Count <= AppLog.MemoryCapacity);
        await log.DisposeAsync();
    }

    [Fact]
    public async Task Diagnostic_report_contains_expected_entries_and_redacts_secrets()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var log = new AppLog(Path.Combine(dir.FullName, "logs"));
            log.Info("Test", "something happened");
            log.Info("Test", "client entered pin 2468");
            await Task.Delay(200);
            var zip = Path.Combine(dir.FullName, "report.zip");
            DiagnosticsReport.Write(zip, new AppSettings(), log,
                [new NetworkAdapterInfo("id", "WLAN", "Intel", AdapterKind.WiFi, true, true, ["192.168.1.2"], [], 0)],
                new Dictionary<string, string> { ["Extra:"] = "value" });
            await log.DisposeAsync();
            using var archive = ZipFile.OpenRead(zip);
            var names = archive.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("system.txt", names);
            Assert.Contains("network.txt", names);
            Assert.Contains("settings.json", names);
            Assert.Contains("log-recent.txt", names);
            foreach (var entry in archive.Entries)
            {
                using var r = new StreamReader(entry.Open());
                Assert.DoesNotContain("2468", r.ReadToEnd());
            }
        }
        finally { dir.Delete(true); }
    }
}

public class ReceiverControllerTests
{
    private sealed class FakeProcess : IReceiverProcess
    {
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Id { get; } = Random.Shared.Next(1000, 9999);
        public bool HasExited { get; private set; }
        public int? ExitCode { get; private set; }
        public bool Disposed { get; private set; }
        public List<string> Input { get; } = [];
        public bool ExitOnQuit { get; set; } = true;
        public event Action<string>? OutputLine;
        public event Action<string>? ErrorLine;
        public event Action? Exited;

        public void Emit(string line) => OutputLine?.Invoke(line);
        public void EmitError(string line) => ErrorLine?.Invoke(line);
        public void Crash(int code = 3) { HasExited = true; ExitCode = code; _exited.TrySetResult(); Exited?.Invoke(); }

        public void SendLine(string line)
        {
            Input.Add(line);
            if (line == "quit" && ExitOnQuit) Crash(0);
        }
        public void CloseInput() { }
        public async Task<bool> WaitForExitAsync(TimeSpan timeout) => await Task.WhenAny(_exited.Task, Task.Delay(timeout)) == _exited.Task;
        public void Kill() => Crash(-1);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory : IReceiverProcessFactory
    {
        public List<FakeProcess> Started { get; } = [];
        public List<ReceiverLaunch> Launches { get; } = [];
        public IReceiverProcess Start(ReceiverLaunch launch)
        {
            Launches.Add(launch);
            var p = new FakeProcess();
            lock (Started) Started.Add(p);
            return p;
        }
        public Task<string> RunToCompletionAsync(ReceiverLaunch launch, TimeSpan timeout) => Task.FromResult("UxPlay 1.74\n-n x\n-nh x\n-s x\n-fps x\n-vs x\n-as x\n-al x\n-reset x\n-nohold x\n-key x\n-vsync x\n-d x\n");
    }

    private static (ReceiverController, FakeFactory, string) Create()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var exe = Path.Combine(dir, "uxplay.exe");
        File.WriteAllText(exe, "fake");
        var factory = new FakeFactory();
        var controller = new ReceiverController(factory, new AppLog(null),
            new ReceiverEnvironment(exe, Path.Combine(dir, "data"), new Dictionary<string, string> { ["GST_X"] = "1" }));
        return (controller, factory, dir);
    }

    private static async Task<ReceiverSnapshot> WaitFor(ReceiverController c, Func<ReceiverSnapshot, bool> predicate, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate(c.Snapshot)) return c.Snapshot;
            await Task.Delay(20);
        }
        throw new TimeoutException("state not reached: " + c.Snapshot.State);
    }

    [Fact]
    public async Task Start_writes_startup_file_and_reports_ready()
    {
        var (c, f, dir) = Create();
        await c.StartAsync(new ReceiverStartRequest(new AppSettings { DeviceName = "Büro PC" }.Normalize(), 0x1234, "192.168.1.5"));
        var launch = Assert.Single(f.Launches);
        Assert.Equal("-rc", launch.Arguments[0]);
        Assert.Equal("1", launch.Environment["DXMIRROR_EVENTS"]);
        Assert.Equal("4660", launch.Environment["DXMIRROR_HWND"]);
        Assert.Equal("192.168.1.5", launch.Environment["DXMIRROR_MDNS_IPV4"]);
        Assert.Equal("1", launch.Environment["GST_X"]);
        var rc = await File.ReadAllTextAsync(launch.Arguments[1]);
        Assert.Contains("n 'Büro PC'", rc);

        f.Started[0].Emit("@@DXM started version=1.74");
        await WaitFor(c, s => s.State == ReceiverState.Ready);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Stop_sends_quit_and_releases_process()
    {
        var (c, f, dir) = Create();
        await c.StartAsync(new ReceiverStartRequest(new AppSettings().Normalize(), 0, null));
        var p = f.Started[0];
        await c.StopAsync();
        Assert.Contains("quit", p.Input);
        Assert.True(p.Disposed);
        await WaitFor(c, s => s.State == ReceiverState.Stopped);
        Assert.False(c.IsRunning);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Process_that_ignores_quit_is_killed()
    {
        var (c, f, dir) = Create();
        await c.StartAsync(new ReceiverStartRequest(new AppSettings().Normalize(), 0, null));
        var p = f.Started[0];
        p.ExitOnQuit = false;
        await c.StopAsync();
        Assert.True(p.HasExited);
        Assert.Equal(-1, p.ExitCode);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Crash_triggers_automatic_restart()
    {
        var (c, f, dir) = Create();
        await c.StartAsync(new ReceiverStartRequest(new AppSettings { AutoReconnect = true }.Normalize(), 0, null));
        f.Started[0].Crash();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (f.Started.Count < 2 && sw.ElapsedMilliseconds < 5000) await Task.Delay(20);
        Assert.Equal(2, f.Started.Count);
        Assert.True(f.Started[0].Disposed);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Crash_without_auto_reconnect_faults()
    {
        var (c, f, dir) = Create();
        await c.StartAsync(new ReceiverStartRequest(new AppSettings { AutoReconnect = false }.Normalize(), 0, null));
        f.Started[0].EmitError("mdns: could not bind UDP port 5353");
        f.Started[0].Crash();
        var s = await WaitFor(c, x => x.State == ReceiverState.Faulted);
        Assert.Equal("mdns_port_blocked", s.LastError);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Missing_executable_faults_without_starting()
    {
        var f = new FakeFactory();
        var c = new ReceiverController(f, new AppLog(null),
            new ReceiverEnvironment(Path.Combine(Path.GetTempPath(), "does-not-exist", "uxplay.exe"), Path.GetTempPath(), new Dictionary<string, string>()));
        await c.StartAsync(new ReceiverStartRequest(new AppSettings().Normalize(), 0, null));
        var s = await WaitFor(c, x => x.State == ReceiverState.Faulted);
        Assert.Equal("receiver_missing", s.LastError);
        Assert.Empty(f.Started);
        await c.DisposeAsync();
    }

    [Fact]
    public async Task Events_from_old_process_are_ignored_after_restart()
    {
        var (c, f, dir) = Create();
        var req = new ReceiverStartRequest(new AppSettings().Normalize(), 0, null);
        await c.StartAsync(req);
        var old = f.Started[0];
        await c.StartAsync(req);
        old.Emit("@@DXM client_request name=\"ghost\" model=\"x\"");
        f.Started[1].Emit("@@DXM started version=1.74");
        var s = await WaitFor(c, x => x.State == ReceiverState.Ready);
        Assert.Null(s.ClientName);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Theory]
    [InlineData(1.0, false, "volume 1.0000")]
    [InlineData(0.5, false, "volume 0.2500")]
    [InlineData(0.8, true, "volume 0.0000")]
    [InlineData(0.0, false, "volume 0.0000")]
    public void Volume_command_uses_perceptual_curve(double volume, bool muted, string expected) =>
        Assert.Equal(expected, ReceiverController.VolumeCommand(volume, muted));

    [Fact]
    public async Task Recording_commands_are_sent_with_forward_slashes()
    {
        var (c, f, dir) = Create();
        Assert.False(c.StartRecording(@"C:\x\y"));   // not running yet
        await c.StartAsync(new ReceiverStartRequest(new AppSettings().Normalize(), 0, null));
        Assert.True(c.StartRecording(@"C:\Users\Leon\Videos\DXMirror\DXMirror_2026"));
        Assert.Contains("record_start C:/Users/Leon/Videos/DXMirror/DXMirror_2026", f.Started[0].Input);
        c.StopRecording();
        Assert.Equal("record_stop", f.Started[0].Input[^1]);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Start_sends_initial_volume_and_changes_are_forwarded()
    {
        var (c, f, dir) = Create();
        await c.StartAsync(new ReceiverStartRequest(new AppSettings { Volume = 0.5 }.Normalize(), 0, null));
        Assert.Contains("volume 0.2500", f.Started[0].Input);
        c.SetVolume(1.0, muted: true);
        Assert.Equal("volume 0.0000", f.Started[0].Input[^1]);
        await c.DisposeAsync();
        Directory.Delete(dir, true);
    }

    [Theory]
    [InlineData("failed to bind 5353", "mdns_port_blocked")]
    [InlineData("Error initializing raop!", "port_in_use")]
    [InlineData("no element \"d3d11videosink\"", "gstreamer_plugin_missing")]
    [InlineData("segmentation fault", "receiver_crashed")]
    [InlineData("unknown option -pin1234, stopping", "invalid_option")]
    [InlineData("invalid \"-pin 0\"; -pin nnnn", "invalid_option")]
    public void Failure_classification(string stderr, string expected) =>
        Assert.Equal(expected, ReceiverController.ClassifyFailure(stderr));
}
