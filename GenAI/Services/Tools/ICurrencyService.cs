using GenAI.Models.Tools;

namespace GenAI.Services.Tools
{
    /// <summary>Converts monetary amounts between currencies using published reference rates.</summary>
    public interface ICurrencyService
    {
        /// <summary>Converts <paramref name="amount"/> from one ISO-4217 currency to another.</summary>
        Task<CurrencyConversionResult> ConvertAsync(
            decimal amount,
            string fromCurrency,
            string toCurrency,
            CancellationToken cancellationToken = default);
    }
}
