namespace Trafty.Core.Weather;

/// <summary>
/// Raised when a weather/color table file (currently: .col) does not match its expected
/// layout.
/// </summary>
public sealed class WeatherFormatException : Exception
{
    public WeatherFormatException(string message) : base(message)
    {
    }

    public WeatherFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
