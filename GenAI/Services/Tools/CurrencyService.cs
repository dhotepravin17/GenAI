using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GenAI.Models.Tools;

namespace GenAI.Services.Tools
{
    /// <summary>
    /// Currency conversion backed by the ExchangeRate-API open endpoint
    /// (no API key required, 160+ currencies including Gulf currencies).
    /// </summary>
    public sealed partial class CurrencyService : ICurrencyService
    {
        /// <summary>Name of the configured <see cref="HttpClient"/>.</summary>
        public const string HttpClientName = "exchange-rates";

        private const string LatestRatesUrl = "https://open.er-api.com/v6/latest/";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CurrencyService> _logger;

        public CurrencyService(IHttpClientFactory httpClientFactory, ILogger<CurrencyService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<CurrencyConversionResult> ConvertAsync(
            decimal amount,
            string fromCurrency,
            string toCurrency,
            CancellationToken cancellationToken = default)
        {
            if (amount <= 0)
            {
                return new CurrencyConversionResult { Error = "The amount must be greater than zero." };
            }

            // Validate model-supplied codes before building the upstream request.
            if (!IsCurrencyCode(fromCurrency) || !IsCurrencyCode(toCurrency))
            {
                return new CurrencyConversionResult
                {
                    Error = "Currency codes must be three letters, for example USD, EUR or QAR."
                };
            }

            var from = fromCurrency.ToUpperInvariant();
            var to = toCurrency.ToUpperInvariant();

            if (from == to)
            {
                return new CurrencyConversionResult
                {
                    Amount = amount,
                    FromCurrency = from,
                    ToCurrency = to,
                    ConvertedAmount = amount,
                    Rate = 1m,
                    RateDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);

            try
            {
                // Read the body even on a failure status: the API reports an unsupported
                // currency as a 404 with a JSON error payload, which is a user error
                // rather than an outage and deserves a different message.
                using var response = await client.GetAsync(LatestRatesUrl + from, cancellationToken);
                var payload = await response.Content.ReadFromJsonAsync<ExchangeRateResponse>(cancellationToken);

                if (payload?.Result != "success" || payload.Rates is null)
                {
                    if (string.Equals(payload?.ErrorType, "unsupported-code", StringComparison.OrdinalIgnoreCase))
                    {
                        return new CurrencyConversionResult { Error = $"'{from}' is not a supported currency code." };
                    }

                    _logger.LogError(
                        "Currency API returned {Status} with result {Result} for {From}.",
                        (int)response.StatusCode,
                        payload?.Result,
                        from);

                    return new CurrencyConversionResult { Error = "The currency service is currently unavailable." };
                }

                if (!payload.Rates.TryGetValue(to, out var rate))
                {
                    return new CurrencyConversionResult { Error = $"'{to}' is not a supported currency code." };
                }

                return new CurrencyConversionResult
                {
                    Amount = amount,
                    FromCurrency = from,
                    ToCurrency = to,
                    ConvertedAmount = decimal.Round(amount * rate, 2),
                    Rate = decimal.Round(rate, 6),
                    RateDate = payload.LastUpdatedUtc
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Upstream detail stays in the logs; the agent gets a short, safe message.
                _logger.LogError(ex, "Currency conversion {From}->{To} failed.", from, to);
                return new CurrencyConversionResult { Error = "The currency service is currently unavailable." };
            }
        }

        private static bool IsCurrencyCode(string? value) =>
            !string.IsNullOrWhiteSpace(value) && CurrencyCodeRegex().IsMatch(value);

        [GeneratedRegex("^[A-Za-z]{3}$")]
        private static partial Regex CurrencyCodeRegex();

        private sealed class ExchangeRateResponse
        {
            [JsonPropertyName("result")]
            public string? Result { get; init; }

            [JsonPropertyName("error-type")]
            public string? ErrorType { get; init; }

            [JsonPropertyName("time_last_update_utc")]
            public string? LastUpdatedUtc { get; init; }

            [JsonPropertyName("rates")]
            public Dictionary<string, decimal>? Rates { get; init; }
        }
    }
}
