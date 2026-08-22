using System.ComponentModel;
using System.Diagnostics;
using GenAI.Models.Agent;
using GenAI.Models.Tools;
using GenAI.Services.Agent;
using Microsoft.Extensions.AI;

namespace GenAI.Services.Tools
{
    /// <summary>
    /// Functions the agent may call. Descriptions here are read by the model to decide
    /// when to invoke a tool, so they must state clearly what each one does.
    /// </summary>
    public sealed class AgentTools
    {
        private readonly IWeatherService _weatherService;
        private readonly ICurrencyService _currencyService;
        private readonly IAgentTraceRecorder _trace;
        private readonly ILogger<AgentTools> _logger;

        public AgentTools(
            IWeatherService weatherService,
            ICurrencyService currencyService,
            IAgentTraceRecorder trace,
            ILogger<AgentTools> logger)
        {
            _weatherService = weatherService;
            _currencyService = currencyService;
            _trace = trace;
            _logger = logger;
        }

        /// <summary>Builds the tool list handed to the agent.</summary>
        public IList<AITool> CreateTools() =>
        [
            AIFunctionFactory.Create(GetWeatherAsync, name: "get_weather"),
            AIFunctionFactory.Create(ConvertCurrencyAsync, name: "convert_currency")
        ];

        [Description("Gets the current weather conditions for a city or place. Use this whenever the user asks about weather, temperature, humidity or wind.")]
        private async Task<WeatherResult> GetWeatherAsync(
            [Description("City or place name, for example 'Doha' or 'Pune, India'.")] string location,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Tool get_weather invoked for {Location}.", location);
            _trace.Record(AgentTraceCategory.Tool, "Model called get_weather", $"location: \"{location}\"");

            var started = Stopwatch.StartNew();
            var result = await _weatherService.GetCurrentWeatherAsync(location, cancellationToken);
            started.Stop();

            _trace.Record(
                AgentTraceCategory.Tool,
                "get_weather returned",
                result.Error ?? $"{result.Location}: {result.Temperature}{result.TemperatureUnit}, {result.Conditions}",
                (int)started.ElapsedMilliseconds);

            return result;
        }

        [Description("Converts an amount of money from one currency to another using current exchange rates. Use this whenever the user asks to convert or compare currencies.")]
        private async Task<CurrencyConversionResult> ConvertCurrencyAsync(
            [Description("The amount of money to convert, for example 100.")] decimal amount,
            [Description("Source currency as a three letter ISO-4217 code, for example USD.")] string fromCurrency,
            [Description("Target currency as a three letter ISO-4217 code, for example INR.")] string toCurrency,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Tool convert_currency invoked for {Amount} {From}->{To}.",
                amount,
                fromCurrency,
                toCurrency);

            _trace.Record(
                AgentTraceCategory.Tool,
                "Model called convert_currency",
                $"amount: {amount}, from: {fromCurrency}, to: {toCurrency}");

            var started = Stopwatch.StartNew();
            var result = await _currencyService.ConvertAsync(amount, fromCurrency, toCurrency, cancellationToken);
            started.Stop();

            _trace.Record(
                AgentTraceCategory.Tool,
                "convert_currency returned",
                result.Error ?? $"{result.Amount} {result.FromCurrency} = {result.ConvertedAmount} {result.ToCurrency} (rate {result.Rate})",
                (int)started.ElapsedMilliseconds);

            return result;
        }
    }
}
