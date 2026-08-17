namespace GenAI.Models.Tools
{
    /// <summary>Result of a currency conversion performed by the currency tool.</summary>
    public sealed class CurrencyConversionResult
    {
        /// <summary>Amount that was converted.</summary>
        public decimal? Amount { get; init; }

        /// <summary>Source currency ISO-4217 code, e.g. "USD".</summary>
        public string? FromCurrency { get; init; }

        /// <summary>Target currency ISO-4217 code, e.g. "INR".</summary>
        public string? ToCurrency { get; init; }

        /// <summary>Converted amount in the target currency.</summary>
        public decimal? ConvertedAmount { get; init; }

        /// <summary>Exchange rate applied (target units per one source unit).</summary>
        public decimal? Rate { get; init; }

        /// <summary>Date of the published reference rate.</summary>
        public string? RateDate { get; init; }

        /// <summary>
        /// Set when the conversion could not be completed, e.g. an unsupported currency code.
        /// The agent relays this to the user instead of a numeric result.
        /// </summary>
        public string? Error { get; init; }
    }
}
