using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DXMirror.App.Platform;

/// <summary>
/// A Windows job object with KILL_ON_JOB_CLOSE: if DXMirror crashes or is killed, Windows
/// terminates the receiver process too, so no orphaned uxplay.exe keeps the AirPlay ports open.
/// </summary>
internal sealed class ChildProcessJob : IDisposable
{
    private nint _handle;

    public ChildProcessJob()
    {
        _handle = NativeMethods.CreateJobObject(0, null);
        if (_handle == 0) return;
        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!NativeMethods.SetInformationJobObject(_handle, NativeMethods.JobObjectExtendedLimitInformation,
                ref info, Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            CloseHandle(_handle);
            _handle = 0;
        }
    }

    public bool Assign(Process process)
    {
        if (_handle == 0) return false;
        try { return NativeMethods.AssignProcessToJobObject(_handle, process.Handle); }
        catch (InvalidOperationException) { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    public void Dispose()
    {
        if (_handle != 0)
        {
            CloseHandle(_handle);
            _handle = 0;
        }
    }
}
