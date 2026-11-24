using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeskCiv;

/// <summary>Watches the Windows audio mixer and reports when another program is making sound.</summary>
internal sealed class AudioYield : IDisposable
{
    private const float AudiblePeak = 0.01f;
    private const int PollMilliseconds = 1500, HoldPolls = 5;

    private readonly Thread _thread;
    private volatile bool _running = true, _othersPlaying;

    public bool OthersPlaying => _othersPlaying;

    public AudioYield()
    {
        _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "DeskCiv audio yield" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(2500);
    }

    private void Run()
    {
        int hold = 0;
        while (_running)
        {
            bool heard = false;
            try { heard = AnotherSessionAudible(); }
            catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException) { }
            hold = heard ? HoldPolls : Math.Max(0, hold - 1);
            _othersPlaying = hold > 0;
            Thread.Sleep(PollMilliseconds);
        }
    }

    private static bool AnotherSessionAudible()
    {
        uint self = (uint)Environment.ProcessId;
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice device = null;
        IAudioSessionManager2 manager = null;
        IAudioSessionEnumerator sessions = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null) return false;
            Guid iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, 23, IntPtr.Zero, out object activated) != 0) return false;
            manager = (IAudioSessionManager2)activated;
            if (manager.GetSessionEnumerator(out sessions) != 0 || sessions == null) return false;
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out IAudioSessionControl2 session) != 0 || session == null) continue;
                try
                {
                    session.GetProcessId(out uint process);
                    if (process == self || process == 0 || session.IsSystemSoundsSession() == 0) continue;
                    if (((IAudioMeterInformation)session).GetPeakValue(out float peak) == 0 && peak >= AudiblePeak) return true;
                }
                finally { Marshal.ReleaseComObject(session); }
            }
            return false;
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            if (manager != null) Marshal.ReleaseComObject(manager);
            if (device != null) Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(IntPtr group, IntPtr context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint process);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
}
