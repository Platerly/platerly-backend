namespace Platerly.Api.Models
{
    /// <summary>
    /// Nickel content classification per serving based on clinical thresholds.
    /// LOW: under 10 μg per serving
    /// MODERATE: 10–20 μg per serving
    /// HIGH: over 20 μg per serving
    /// Source: Mislankar &amp; Zirwas 2013, daily threshold 150 μg/day
    /// </summary>
    public enum NickelClassification
    {
        Unknown = 0,
        Low = 1,
        Moderate = 2,
        High = 3
    }

    /// <summary>
    /// Confidence level based on number of data points behind the measurement.
    /// HIGH: 10+ data points
    /// MEDIUM: 3–9 data points
    /// LOW: 1–2 data points
    /// UNKNOWN: no data
    /// </summary>
    public enum NickelConfidenceLevel
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }
}
