namespace GenAI.Models.Tools
{
    /// <summary>Current weather conditions returned by the weather tool.</summary>
    public sealed class WeatherResult
    {
        /// <summary>Resolved place name, e.g. "Doha, Qatar".</summary>
        public string? Location { get; init; }

        /// <summary>Current temperature in the reported unit.</summary>
        public double? Temperature { get; init; }

        /// <summary>Unit of <see cref="Temperature"/>, e.g. "°C".</summary>
        public string? TemperatureUnit { get; init; }

        /// <summary>Relative humidity as a percentage.</summary>
        public int? HumidityPercent { get; init; }

        /// <summary>Wind speed in the reported unit.</summary>
        public double? WindSpeed { get; init; }

        /// <summary>Unit of <see cref="WindSpeed"/>, e.g. "km/h".</summary>
        public string? WindSpeedUnit { get; init; }

        /// <summary>Human readable sky conditions, e.g. "Partly cloudy".</summary>
        public string? Conditions { get; init; }

        /// <summary>
        /// Set when the lookup could not be completed, e.g. the place was not found.
        /// The agent relays this to the user instead of a numeric result.
        /// </summary>
        public string? Error { get; init; }
    }
}
