namespace Trafty.Core.Models;

/// <summary>
/// Raised when a 3D model file (currently: .nif) does not follow its expected layout.
/// </summary>
public sealed class ModelFormatException : Exception
{
    public ModelFormatException(string message) : base(message)
    {
    }

    public ModelFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
