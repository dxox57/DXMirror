using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace DXMirror.Core.Diagnostics;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
}

public sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Source, string Message)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Time.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff} [{Level.ToString().ToUpperInvariant(),-7}] {Source}: {Message}");
}

/// <summary>
/// Application log: thread-safe, non-blocking for callers (entries go through a bounded channel
/// to a background writer), redacts secrets, keeps the last N entries in memory for the UI and
/// writes rotating log files (max <see cref="MaxFileBytes"/> × <see cref="MaxFiles"/>).
/// </summary>
public sealed class AppLog : IAsyncDisposable
{
    public const int MemoryCapacity = 2000;
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxFiles = 5;

    private readonly Channel<LogEntry> _channel;
    private readonly ConcurrentQueue<LogEntry> _recent = new();
    private readonly Task _writer;
    private readonly string? _directory;
    private long _dropped;

    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
    public string? Directory => _directory;
    public string? CurrentFile => _directory is null ? null : Path.Combine(_directory, "dxmirror.log");
    public long DroppedEntries => Interlocked.Read(ref _dropped);

    public event Action<LogEntry>? EntryAdded;

    public AppLog(string? directory)
    {
        _directory = directory;
        if (_directory is not null) System.IO.Directory.CreateDirectory(_directory);
        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        _writer = Task.Run(WriteLoopAsync);
    }

    public void Debug(string source, string message) => Write(LogLevel.Debug, source, message);
    public void Info(string source, string message) => Write(LogLevel.Info, source, message);
    public void Warn(string source, string message) => Write(LogLevel.Warning, source, message);
    public void Error(string source, string message, Exception? ex = null) =>
        Write(LogLevel.Error, source, ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    public void Write(LogLevel level, string source, string message)
    {
        if (level < MinimumLevel) return;
        var entry = new LogEntry(DateTimeOffset.Now, level, source, LogRedactor.Redact(message));
        _recent.Enqueue(entry);
        while (_recent.Count > MemoryCapacity && _recent.TryDequeue(out _)) { }
        if (!_channel.Writer.TryWrite(entry)) Interlocked.Increment(ref _dropped);
        try { EntryAdded?.Invoke(entry); } catch { /* UI listener must never break logging */ }
    }

    public IReadOnlyList<LogEntry> Recent() => _recent.ToArray();

    private async Task WriteLoopAsync()
    {
        if (_directory is null)
        {
            await foreach (var _ in _channel.Reader.ReadAllAsync().ConfigureAwait(false)) { }
            return;
        }
        StreamWriter? writer = null;
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    writer ??= OpenWriter();
                    await writer.WriteLineAsync(entry.ToString()).ConfigureAwait(false);
                    if (_channel.Reader.Count == 0) await writer.FlushAsync().ConfigureAwait(false);
                    if (writer.BaseStream.Length > MaxFileBytes)
                    {
                        await writer.DisposeAsync().ConfigureAwait(false);
                        writer = null;
                        Rotate();
                    }
                }
                catch (IOException)
                {
                    writer?.Dispose();
                    writer = null;
                }
            }
        }
        finally
        {
            if (writer is not null) await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private StreamWriter OpenWriter()
    {
        var fs = new FileStream(CurrentFile!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        return new StreamWriter(fs, new UTF8Encoding(false));
    }

    private void Rotate()
    {
        for (int i = MaxFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(_directory!, i == 1 ? "dxmirror.log" : $"dxmirror.{i - 1}.log");
            var dst = Path.Combine(_directory!, $"dxmirror.{i}.log");
            try { if (File.Exists(src)) File.Move(src, dst, overwrite: true); } catch (IOException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try { await _writer.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException) { }
    }
}
