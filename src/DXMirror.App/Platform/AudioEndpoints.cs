using System.Runtime.InteropServices;

namespace DXMirror.App.Platform;

public sealed record AudioOutputDevice(string Id, string Name, bool IsDefault);

/// <summary>
/// Minimal Windows Core Audio (MMDevice / WASAPI session) interop:
///  * list active playback devices (ids are the same endpoint ids GStreamer's wasapisink accepts),
///  * set volume / mute of the receiver process' own audio session (does not touch the system volume).
/// </summary>
internal static class AudioEndpoints
{
    public static IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var list = new List<AudioOutputDevice>();
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var def) == 0 && def is not null)
            {
                def.GetId(out defaultId);
                Marshal.ReleaseComObject(def);
            }
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DEVICE_STATE_ACTIVE, out var collection) != 0) return list;
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0) continue;
                try
                {
                    device.GetId(out var id);
                    list.Add(new AudioOutputDevice(id, GetFriendlyName(device) ?? id, id == defaultId));
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            Marshal.ReleaseComObject(collection);
        }
        catch (COMException) { }
        catch (InvalidCastException) { }
        finally
        {
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
        return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Applies volume / mute to every audio session that belongs to <paramref name="processId"/> on any
    /// active playback device. Returns the number of sessions changed (0 = the receiver has not opened
    /// an audio stream yet; call again when audio starts).
    /// </summary>
    public static int SetProcessVolume(int processId, float volume, bool mute)
    {
        int changed = 0;
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DEVICE_STATE_ACTIVE, out var collection) != 0) return 0;
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0) continue;
                try { changed += ApplyOnDevice(device, processId, volume, mute); }
                finally { Marshal.ReleaseComObject(device); }
            }
            Marshal.ReleaseComObject(collection);
        }
        catch (COMException) { }
        catch (InvalidCastException) { }
        finally
        {
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
        return changed;
    }

    private static int ApplyOnDevice(IMMDevice device, int processId, float volume, bool mute)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref iid, CLSCTX_ALL, 0, out var obj) != 0 || obj is not IAudioSessionManager2 manager) return 0;
        int changed = 0;
        try
        {
            if (manager.GetSessionEnumerator(out var sessions) != 0) return 0;
            sessions.GetCount(out var n);
            for (int i = 0; i < n; i++)
            {
                if (sessions.GetSession(i, out var control) != 0) continue;
                try
                {
                    if (control is IAudioSessionControl2 c2 && c2.GetProcessId(out var pid) == 0 && pid == (uint)processId
                        && control is ISimpleAudioVolume simple)
                    {
                        var ctx = Guid.Empty;
                        simple.SetMasterVolume(Math.Clamp(volume, 0f, 1f), ref ctx);
                        simple.SetMute(mute ? 1 : 0, ref ctx);
                        changed++;
                    }
                }
                finally { Marshal.ReleaseComObject(control); }
            }
            Marshal.ReleaseComObject(sessions);
        }
        finally { Marshal.ReleaseComObject(manager); }
        return changed;
    }

    private static string? GetFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0) return null;
        try
        {
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try
            {
                return pv.vt == VT_LPWSTR && pv.pointerValue != 0 ? Marshal.PtrToStringUni(pv.pointerValue) : null;
            }
            finally { PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    // ------------------------------------------------------------------ COM definitions
    private const int DEVICE_STATE_ACTIVE = 0x1;
    private const int STGM_READ = 0x0;
    private const int CLSCTX_ALL = 0x17;
    private const ushort VT_LPWSTR = 31;

    private static readonly PROPERTYKEY PKEY_Device_FriendlyName =
        new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint pointerValue;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(nint client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(nint client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager
        [PreserveSig] int GetAudioSessionControl(nint sessionGuid, uint flags, out nint sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(nint sessionGuid, uint flags, out nint audioVolume);
        // IAudioSessionManager2
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
        [PreserveSig] int RegisterSessionNotification(nint notification);
        [PreserveSig] int UnregisterSessionNotification(nint notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, nint notification);
        [PreserveSig] int UnregisterDuckNotification(nint notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out nint name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
        [PreserveSig] int GetIconPath(out nint path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingParam);
        [PreserveSig] int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(nint client);
        [PreserveSig] int UnregisterAudioSessionNotification(nint client);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier(out nint id);
        [PreserveSig] int GetSessionInstanceIdentifier(out nint id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6F0B4F0B9F4A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute(int mute, ref Guid eventContext);
        [PreserveSig] int GetMute(out int mute);
    }
}
