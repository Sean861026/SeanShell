using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class AudioEndpointService
{
    private const int ClsContextAll = 23;
    private static readonly Guid AudioEndpointVolumeId =
        new("5CDF2C82-841E-4546-9722-0CF74078229A");

    public AudioEndpointSnapshot Capture() =>
        UseDefaultEndpoint(Capture);

    public AudioEndpointSnapshot SetVolume(int volumePercent) =>
        UseDefaultEndpoint(
            endpoint =>
            {
                var eventContext = Guid.Empty;
                Marshal.ThrowExceptionForHR(
                    endpoint.SetMasterVolumeLevelScalar(
                        Math.Clamp(volumePercent, 0, 100) / 100f,
                        ref eventContext));
                if (volumePercent > 0)
                {
                    Marshal.ThrowExceptionForHR(
                        endpoint.SetMute(false, ref eventContext));
                }

                return Capture(endpoint);
            });

    public AudioEndpointSnapshot SetMuted(bool muted) =>
        UseDefaultEndpoint(
            endpoint =>
            {
                var eventContext = Guid.Empty;
                Marshal.ThrowExceptionForHR(
                    endpoint.SetMute(muted, ref eventContext));
                return Capture(endpoint);
            });

    private static AudioEndpointSnapshot Capture(
        IAudioEndpointVolume endpoint)
    {
        Marshal.ThrowExceptionForHR(
            endpoint.GetMasterVolumeLevelScalar(out var scalar));
        Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
        return new AudioEndpointSnapshot(
            IsAvailable: true,
            VolumePercent: (int)Math.Round(
                Math.Clamp(scalar, 0f, 1f) * 100f,
                MidpointRounding.AwayFromZero),
            IsMuted: muted);
    }

    private static AudioEndpointSnapshot UseDefaultEndpoint(
        Func<IAudioEndpointVolume, AudioEndpointSnapshot> operation)
    {
        object? enumeratorObject = null;
        IMMDevice? device = null;
        object? endpointObject = null;

        try
        {
            enumeratorObject = new MMDeviceEnumeratorComObject();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            Marshal.ThrowExceptionForHR(
                enumerator.GetDefaultAudioEndpoint(
                    AudioDataFlow.Render,
                    AudioRole.Multimedia,
                    out device));

            var interfaceId = AudioEndpointVolumeId;
            Marshal.ThrowExceptionForHR(
                device.Activate(
                    ref interfaceId,
                    ClsContextAll,
                    IntPtr.Zero,
                    out endpointObject));
            return operation((IAudioEndpointVolume)endpointObject);
        }
        catch (Exception exception) when (
            exception is COMException or
            InvalidCastException or
            PlatformNotSupportedException)
        {
            return new AudioEndpointSnapshot(
                IsAvailable: false,
                VolumePercent: null,
                IsMuted: false);
        }
        finally
        {
            ReleaseComObject(endpointObject);
            ReleaseComObject(device);
            ReleaseComObject(enumeratorObject);
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }

    private enum AudioDataFlow
    {
        Render,
        Capture,
        All,
    }

    private enum AudioRole
    {
        Console,
        Multimedia,
        Communications,
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(
            AudioDataFlow dataFlow,
            uint stateMask,
            out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(
            AudioDataFlow dataFlow,
            AudioRole role,
            out IMMDevice device);

        [PreserveSig]
        int GetDevice(
            [MarshalAs(UnmanagedType.LPWStr)] string id,
            out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid interfaceId,
            int classContext,
            IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(uint accessMode, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr notify);

        [PreserveSig]
        int GetChannelCount(out uint channelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float levelDb);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig]
        int SetChannelVolumeLevel(
            uint channel,
            float levelDb,
            ref Guid eventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(
            uint channel,
            float level,
            ref Guid eventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint channel, out float levelDb);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute(
            [MarshalAs(UnmanagedType.Bool)] bool muted,
            ref Guid eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
