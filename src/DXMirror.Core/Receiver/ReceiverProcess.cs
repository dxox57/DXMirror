using System.Diagnostics;
using System.Text;

namespace DXMirror.Core.Receiver;

/// <summary>Abstraction of the receiver child process so the controller can be unit-tested.</summary>
public interface IReceiverProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
    event Action<string>? OutputLine;
    event Action<string>? ErrorLine;
    event Action? Exited;
    void SendLine(string line);
    void CloseInput();
    Task<bool> WaitForExitAsync(TimeSpan timeout);
    void Kill();
}

public sealed record ReceiverLaunch(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string WorkingDirectory);

public interface IReceiverProcessFactory
{
    IReceiverProcess Start(ReceiverLaunch launch);

    /// <summary>Runs the executable with the given arguments and returns its combined output (used for "-h").</summary>
    Task<string> RunToCompletionAsync(ReceiverLaunch launch, TimeSpan timeout);
}

/// <summary>Real implementation using <see cref="System.Diagnostics.Process"/>.</summary>
public sealed class SystemReceiverProcessFactory : IReceiverProcessFactory
{
    /// <summary>Called right after the process started (e.g. to assign it to a Windows job object).</summary>
    public Action<Process>? OnProcessStarted { get; set; }

    public IReceiverProcess Start(ReceiverLaunch launch)
    {
        var psi = CreateStartInfo(launch);
        psi.RedirectStandardInput = true;
        psi.StandardInputEncoding = new UTF8Encoding(false);
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var wrapper = new SystemReceiverProcess(p);
        if (!p.Start()) throw new InvalidOperationException("Receiver process could not be started.");
        OnProcessStarted?.Invoke(p);
        wrapper.BeginReading();
        return wrapper;
    }

    public async Task<string> RunToCompletionAsync(ReceiverLaunch launch, TimeSpan timeout)
    {
        var psi = CreateStartInfo(launch);
        using var p = new Process { StartInfo = psi };
        p.Start();
        OnProcessStarted?.Invoke(p);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return string.Empty;
        }
        return await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false);
    }

    private static ProcessStartInfo CreateStartInfo(ReceiverLaunch launch)
    {
        var psi = new ProcessStartInfo(launch.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = launch.WorkingDirectory,
        };
        foreach (var a in launch.Arguments) psi.ArgumentList.Add(a);
        foreach (var (k, v) in launch.Environment) psi.Environment[k] = v;
        return psi;
    }
}

internal sealed class SystemReceiverProcess : IReceiverProcess
{
    private readonly Process _p;
    private readonly object _inputGate = new();
    private int _disposed;

    public SystemReceiverProcess(Process p)
    {
        _p = p;
        _p.Exited += (_, _) => Exited?.Invoke();
    }

    public int Id => _p.Id;
    public bool HasExited { get { try { return _p.HasExited; } catch (InvalidOperationException) { return true; } } }
    public int? ExitCode { get { try { return _p.HasExited ? _p.ExitCode : null; } catch (InvalidOperationException) { return null; } } }

    public event Action<string>? OutputLine;
    public event Action<string>? ErrorLine;
    public event Action? Exited;

    internal void BeginReading()
    {
        _p.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputLine?.Invoke(e.Data); };
        _p.ErrorDataReceived += (_, e) => { if (e.Data is not null) ErrorLine?.Invoke(e.Data); };
        _p.BeginOutputReadLine();
        _p.BeginErrorReadLine();
    }

    public void SendLine(string line)
    {
        lock (_inputGate)
        {
            try
            {
                _p.StandardInput.WriteLine(line);
                _p.StandardInput.Flush();
            }
            catch (IOException) { }
            catch (InvalidOperationException) { } // also covers ObjectDisposedException
        }
    }

    public void CloseInput()
    {
        lock (_inputGate)
        {
            try { _p.StandardInput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
        }
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (InvalidOperationException) { return true; }
    }

    public void Kill()
    {
        try { if (!_p.HasExited) _p.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _p.Dispose();
    }
}
