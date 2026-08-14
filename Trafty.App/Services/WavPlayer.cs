using System.Runtime.InteropServices;

namespace Trafty.App.Services;

/// <summary>
/// Plays a .wav file via the Windows winmm.dll PlaySound API — a P/Invoke call to a system
/// DLL that ships with Windows, not a new package dependency (matches the project's
/// deliberately minimal footprint). DAoC itself is Windows-only, so this covers the actual
/// target platform; on anything else <see cref="IsSupported"/> is false and callers should
/// disable playback rather than attempt it.
/// </summary>
public static class WavPlayer
{
    private const uint SndAsync = 0x0001;
    private const uint SndFilename = 0x00020000;
    private const uint SndNoDefault = 0x0002;

    public static bool IsSupported => OperatingSystem.IsWindows();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySoundW(string? pszSound, nint hmod, uint fdwSound);

    /// <summary>Starts asynchronous playback, replacing any sound currently playing via this API.</summary>
    public static void Play(string path)
    {
        if (!IsSupported)
        {
            return;
        }

        PlaySoundW(path, 0, SndFilename | SndAsync | SndNoDefault);
    }

    public static void Stop()
    {
        if (!IsSupported)
        {
            return;
        }

        PlaySoundW(null, 0, 0);
    }
}
