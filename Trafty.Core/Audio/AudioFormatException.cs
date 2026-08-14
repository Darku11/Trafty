namespace Trafty.Core.Audio;

/// <summary>
/// Raised when an audio file (currently: .wav) does not follow its expected layout.
/// </summary>
public sealed class AudioFormatException : Exception
{
    public AudioFormatException(string message) : base(message)
    {
    }

    public AudioFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
