using System.IO.Pipes;

namespace DXMirror.App.Services;

/// <summary>
/// Ensures only one DXMirror runs per user session (two receivers would fight over the AirPlay
/// ports and the mDNS name). A second start asks the first instance to show its window.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    public bool IsFirstInstance { get; }

    public event Action? ActivationRequested;

    public SingleInstance(string id)
    {
        var user = Environment.UserName.Replace('\\', '_');
        _pipeName = $"{id}-{user}";
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{id}-{user}", out bool created);
        IsFirstInstance = created;
        if (created) _ = ListenAsync(_cts.Token);
    }

    public static bool SignalFirstInstance(string id)
    {
        try
        {
            var user = Environment.UserName.Replace('\\', '_');
            using var client = new NamedPipeClientStream(".", $"{id}-{user}", PipeDirection.Out);
            client.Connect(2000);
            client.WriteByte(1);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { return false; }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var buffer = new byte[1];
                if (await server.ReadAsync(buffer, ct).ConfigureAwait(false) == 1) ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(500, ct).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
        _cts.Dispose();
    }
}
