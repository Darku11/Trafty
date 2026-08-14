namespace Trafty.Core.WorldProps;

/// <summary>
/// Raised when a world-prop file (currently: .nhd) does not follow its expected layout.
/// </summary>
public sealed class WorldPropFormatException : Exception
{
    public WorldPropFormatException(string message) : base(message)
    {
    }

    public WorldPropFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
