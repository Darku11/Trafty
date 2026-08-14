namespace Trafty.Core.Archives;

/// <summary>
/// Raised when a file does not follow the MPAK container layout, or when its structural
/// fields contradict each other (bad magic, impossible offsets, checksum mismatch).
/// </summary>
public sealed class MpkFormatException : Exception
{
    public MpkFormatException(string message)
        : base(message)
    {
    }

    public MpkFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
