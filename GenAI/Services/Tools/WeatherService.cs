using System.Globalization;
using System.Text.Json.Serialization;
using GenAI.Models.Tools;

namespace GenAI.Services.Tools
{
    /// <summary>
    /// Weather lookup backed by the Open-Meteo public API (no API key required).
    /// The place name is geocoded first, then current conditions are fetched.
    /// </summary>
    public sealed class WeatherService : IWeatherService
    {
        /// <summary>Name of the configured <see cref="HttpClient"/>.</summary>
        public const string HttpClientName = "open-meteo";

        private const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name=";
        private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast?current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WeatherService> _logger;

        public WeatherService(IHttpClientFactory httpClientFactory, ILogger<WeatherService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<WeatherResult> GetCurrentWeatherAsync(string location, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                return new WeatherResult { Error = "A location is required." };
            }

            // Guard against oversized model-supplied input before it reaches the upstream API.
            if (location.Length > 100)
            {
                return new WeatherResult { Error = "The location name is too long." };
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);

            try
            {
                var geocoded = await client.GetFromJsonAsync<GeocodingResponse>(
                    GeocodingUrl + Uri.EscapeDataString(location),
                    cancellationToken);

                var place = geocoded?.Results?.FirstOrDefault();
                if (place is null)
                {
                    return new WeatherResult { Error = $"Could not find a place named '{location}'." };
                }

                var forecastUrl = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ForecastUrl}&latitude={place.Latitude}&longitude={place.Longitude}");

                var forecast = await client.GetFromJsonAsync<ForecastResponse>(forecastUrl, cancellationToken);
                if (forecast?.Current is null)
                {
                    return new WeatherResult { Error = "Weather data is not available for that location right now." };
                }

                var displayName = string.IsNullOrWhiteSpace(place.Country)
                    ? place.Name
                    : $"{place.Name}, {place.Country}";

                return new WeatherResult
                {
                    Location = displayName,
                    Temperature = forecast.Current.Temperature,
                    TemperatureUnit = forecast.Units?.Temperature,
                    HumidityPercent = forecast.Current.Humidity,
                    WindSpeed = forecast.Current.WindSpeed,
                    WindSpeedUnit = forecast.Units?.WindSpeed,
                    Conditions = DescribeWeatherCode(forecast.Current.WeatherCode)
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Upstream detail stays in the logs; the agent gets a short, safe message.
                _logger.LogError(ex, "Weather lookup failed for {Location}.", location);
                return new WeatherResult { Error = "The weather service is currently unavailable." };
            }
        }

        /// <summary>Maps a WMO weather code to a short description.</summary>
        private static string DescribeWeatherCode(int? code) => code switch
        {
            0 => "Clear sky",
            1 => "Mainly clear",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Fog",
            51 or 53 or 55 => "Drizzle",
            56 or 57 => "Freezing drizzle",
            61 or 63 or 65 => "Rain",
            66 or 67 => "Freezing rain",
            71 or 73 or 75 => "Snow",
            77 => "Snow grains",
            80 or 81 or 82 => "Rain showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm with hail",
            _ => "Unknown"
        };

        private sealed class GeocodingResponse
        {
            [JsonPropertyName("results")]
            public List<GeocodedPlace>? Results { get; init; }
        }

        private sealed class GeocodedPlace
        {
            [JsonPropertyName("name")]
            public string? Name { get; init; }

            [JsonPropertyName("country")]
            public string? Country { get; init; }

            [JsonPropertyName("latitude")]
            public double Latitude { get; init; }

            [JsonPropertyName("longitude")]
            public double Longitude { get; init; }
        }

        private sealed class ForecastResponse
        {
            [JsonPropertyName("current")]
            public CurrentWeather? Current { get; init; }

            [JsonPropertyName("current_units")]
            public CurrentUnits? Units { get; init; }
        }

        private sealed class CurrentWeather
        {
            [JsonPropertyName("temperature_2m")]
            public double? Temperature { get; init; }

            [JsonPropertyName("relative_humidity_2m")]
            public int? Humidity { get; init; }

            [JsonPropertyName("wind_speed_10m")]
            public double? WindSpeed { get; init; }

            [JsonPropertyName("weather_code")]
            public int? WeatherCode { get; init; }
        }

        private sealed class CurrentUnits
        {
            [JsonPropertyName("temperature_2m")]
            public string? Temperature { get; init; }

            [JsonPropertyName("wind_speed_10m")]
            public string? WindSpeed { get; init; }
        }
    }
}
