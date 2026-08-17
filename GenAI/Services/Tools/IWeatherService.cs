using GenAI.Models.Tools;

namespace GenAI.Services.Tools
{
    /// <summary>Looks up current weather conditions for a place.</summary>
    public interface IWeatherService
    {
        /// <summary>Gets current conditions for the named place.</summary>
        Task<WeatherResult> GetCurrentWeatherAsync(string location, CancellationToken cancellationToken = default);
    }
}
