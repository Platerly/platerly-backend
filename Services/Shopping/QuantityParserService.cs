using System.Text.RegularExpressions;

namespace Platerly.Api.Services.Shopping
{
    /// <summary>
    /// Parses and consolidates ingredient quantities.
    /// Handles fractions, decimals, ranges, and unit normalization.
    /// </summary>
    public interface IQuantityParserService
    {
        /// <summary>
        /// Parse a quantity string into numeric amount and unit
        /// </summary>
        ParsedQuantity Parse(string? quantity);

        /// <summary>
        /// Consolidate multiple quantities of the same ingredient
        /// </summary>
        ConsolidatedQuantity Consolidate(IEnumerable<ParsedQuantity> quantities);

        /// <summary>
        /// Format a numeric amount and unit back to display string
        /// </summary>
        string Format(decimal amount, string? unit);
    }

    public class ParsedQuantity
    {
        public decimal? Amount { get; set; }
        public string? Unit { get; set; }
        public string? OriginalText { get; set; }
        public bool WasParsed { get; set; }
    }

    public class ConsolidatedQuantity
    {
        public decimal TotalAmount { get; set; }
        public string? Unit { get; set; }
        public string DisplayText { get; set; } = string.Empty;
        public bool WasConsolidated { get; set; }
        public List<string> SourceQuantities { get; set; } = new();
    }

    public class QuantityParserService : IQuantityParserService
    {
        // Common unit aliases for normalization
        // Note: Using case-sensitive for T/t distinction (T=tablespoon, t=teaspoon)
        private static readonly Dictionary<string, string> UnitAliases = new()
        {
            // Volume - case-sensitive entries first
            { "T", "tablespoon" },  // Capital T = tablespoon
            { "t", "teaspoon" },    // Lowercase t = teaspoon
            { "c", "cup" }, { "C", "cup" }, { "c.", "cup" }, { "cups", "cup" }, { "Cups", "cup" },
            { "tbsp", "tablespoon" }, { "Tbsp", "tablespoon" }, { "TBSP", "tablespoon" }, { "tbsps", "tablespoon" }, { "tbs", "tablespoon" }, { "tablespoons", "tablespoon" },
            { "tsp", "teaspoon" }, { "Tsp", "teaspoon" }, { "TSP", "teaspoon" }, { "tsps", "teaspoon" }, { "teaspoons", "teaspoon" },
            { "fl oz", "fluid ounce" }, { "fl. oz.", "fluid ounce" }, { "fluid ounces", "fluid ounce" },
            { "pt", "pint" }, { "pts", "pint" }, { "pints", "pint" },
            { "qt", "quart" }, { "qts", "quart" }, { "quarts", "quart" },
            { "gal", "gallon" }, { "gals", "gallon" }, { "gallons", "gallon" },
            { "ml", "milliliter" }, { "mls", "milliliter" }, { "milliliters", "milliliter" }, { "millilitres", "milliliter" },
            { "l", "liter" }, { "liters", "liter" }, { "litres", "liter" },

            // Weight
            { "oz", "ounce" }, { "ozs", "ounce" }, { "ounces", "ounce" },
            { "lb", "pound" }, { "lbs", "pound" }, { "pounds", "pound" },
            { "g", "gram" }, { "gms", "gram" }, { "grams", "gram" }, { "grammes", "gram" },
            { "kg", "kilogram" }, { "kgs", "kilogram" }, { "kilograms", "kilogram" },

            // Count
            { "pc", "piece" }, { "pcs", "piece" }, { "pieces", "piece" },
            { "slice", "slice" }, { "slices", "slice" },
            { "clove", "clove" }, { "cloves", "clove" },
            { "sprig", "sprig" }, { "sprigs", "sprig" },
            { "bunch", "bunch" }, { "bunches", "bunch" },
            { "head", "head" }, { "heads", "head" },
            { "stalk", "stalk" }, { "stalks", "stalk" },
            { "can", "can" }, { "cans", "can" },
            { "package", "package" }, { "packages", "package" }, { "pkg", "package" }, { "pkgs", "package" },
            { "bottle", "bottle" }, { "bottles", "bottle" },
            { "jar", "jar" }, { "jars", "jar" },

            // Size descriptors (these get special handling)
            { "small", "small" }, { "sm", "small" },
            { "medium", "medium" }, { "med", "medium" },
            { "large", "large" }, { "lg", "large" },
        };

        // Fraction patterns
        private static readonly Dictionary<string, decimal> FractionValues = new()
        {
            { "1/8", 0.125m }, { "⅛", 0.125m },
            { "1/4", 0.25m }, { "¼", 0.25m },
            { "1/3", 0.333m }, { "⅓", 0.333m },
            { "3/8", 0.375m }, { "⅜", 0.375m },
            { "1/2", 0.5m }, { "½", 0.5m },
            { "5/8", 0.625m }, { "⅝", 0.625m },
            { "2/3", 0.667m }, { "⅔", 0.667m },
            { "3/4", 0.75m }, { "¾", 0.75m },
            { "7/8", 0.875m }, { "⅞", 0.875m },
        };

        // Regex patterns
        private static readonly Regex NumberPattern = new(@"^([\d]+(?:\.[\d]+)?)", RegexOptions.Compiled);
        private static readonly Regex FractionPattern = new(@"(\d+/\d+|[⅛¼⅓⅜½⅝⅔¾⅞])", RegexOptions.Compiled);
        private static readonly Regex MixedNumberPattern = new(@"^(\d+)\s*(?:and\s*)?(\d+/\d+|[⅛¼⅓⅜½⅝⅔¾⅞])", RegexOptions.Compiled);
        private static readonly Regex RangePattern = new(@"(\d+(?:\.\d+)?)\s*[-–to]\s*(\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex UnitPattern = new(@"(?:^|\s)(cups?|tbsps?|tsps?|tablespoons?|teaspoons?|oz|ounces?|lbs?|pounds?|g|grams?|kg|kilograms?|ml|milliliters?|l|liters?|pints?|quarts?|gallons?|pieces?|cloves?|sprigs?|bunche?s?|heads?|stalks?|cans?|packages?|pkgs?|bottles?|jars?|slices?|small|medium|large|sm|med|lg)(?:\s|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ParsedQuantity Parse(string? quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity))
            {
                return new ParsedQuantity { WasParsed = false };
            }

            var result = new ParsedQuantity
            {
                OriginalText = quantity.Trim(),
                WasParsed = false
            };

            var text = quantity.Trim().ToLowerInvariant();
            decimal? amount = null;

            // Try to parse mixed number (e.g., "1 1/2")
            var mixedMatch = MixedNumberPattern.Match(text);
            if (mixedMatch.Success)
            {
                var whole = decimal.Parse(mixedMatch.Groups[1].Value);
                var fractionPart = mixedMatch.Groups[2].Value;
                if (FractionValues.TryGetValue(fractionPart, out var fracValue))
                {
                    amount = whole + fracValue;
                }
                else if (TryParseFraction(fractionPart, out var parsed))
                {
                    amount = whole + parsed;
                }
                text = text.Substring(mixedMatch.Length).Trim();
            }
            else
            {
                // Try range (take average)
                var rangeMatch = RangePattern.Match(text);
                if (rangeMatch.Success)
                {
                    var low = decimal.Parse(rangeMatch.Groups[1].Value);
                    var high = decimal.Parse(rangeMatch.Groups[2].Value);
                    amount = (low + high) / 2;
                    text = text.Substring(rangeMatch.Length).Trim();
                }
                else
                {
                    // Try fraction
                    var fractionMatch = FractionPattern.Match(text);
                    if (fractionMatch.Success && fractionMatch.Index == 0)
                    {
                        if (FractionValues.TryGetValue(fractionMatch.Value, out var fracValue))
                        {
                            amount = fracValue;
                        }
                        else if (TryParseFraction(fractionMatch.Value, out var parsed))
                        {
                            amount = parsed;
                        }
                        text = text.Substring(fractionMatch.Length).Trim();
                    }
                    else
                    {
                        // Try decimal/integer
                        var numberMatch = NumberPattern.Match(text);
                        if (numberMatch.Success)
                        {
                            amount = decimal.Parse(numberMatch.Value);
                            text = text.Substring(numberMatch.Length).Trim();
                        }
                    }
                }
            }

            // Extract unit
            var unitMatch = UnitPattern.Match(text);
            if (unitMatch.Success)
            {
                var rawUnit = unitMatch.Groups[1].Value.ToLowerInvariant();
                result.Unit = NormalizeUnit(rawUnit);
            }

            result.Amount = amount;
            result.WasParsed = amount.HasValue;

            return result;
        }

        public ConsolidatedQuantity Consolidate(IEnumerable<ParsedQuantity> quantities)
        {
            var list = quantities.Where(q => q.WasParsed && q.Amount.HasValue).ToList();

            if (!list.Any())
            {
                return new ConsolidatedQuantity { WasConsolidated = false };
            }

            // Group by normalized unit
            var byUnit = list.GroupBy(q => q.Unit ?? "").ToList();

            if (byUnit.Count == 1)
            {
                // All same unit - simple sum
                var unit = byUnit[0].Key;
                var total = byUnit[0].Sum(q => q.Amount!.Value);

                return new ConsolidatedQuantity
                {
                    TotalAmount = total,
                    Unit = string.IsNullOrEmpty(unit) ? null : unit,
                    DisplayText = Format(total, unit),
                    WasConsolidated = list.Count > 1,
                    SourceQuantities = list.Select(q => q.OriginalText ?? "").ToList()
                };
            }

            // Different units - try to convert, or just list them
            // For now, return the largest group summed, note the others
            var largest = byUnit.OrderByDescending(g => g.Count()).First();
            var largestTotal = largest.Sum(q => q.Amount!.Value);
            var largestUnit = largest.Key;

            var result = new ConsolidatedQuantity
            {
                TotalAmount = largestTotal,
                Unit = string.IsNullOrEmpty(largestUnit) ? null : largestUnit,
                DisplayText = Format(largestTotal, largestUnit),
                WasConsolidated = true,
                SourceQuantities = list.Select(q => q.OriginalText ?? "").ToList()
            };

            // Add unconsolidated items to display
            var others = byUnit.Where(g => g.Key != largestUnit).SelectMany(g => g).ToList();
            if (others.Any())
            {
                var otherText = string.Join(" + ", others.Select(q => q.OriginalText));
                result.DisplayText += $" + {otherText}";
            }

            return result;
        }

        public string Format(decimal amount, string? unit)
        {
            string amountStr;

            // Convert to nice fractions if close
            if (Math.Abs(amount - Math.Floor(amount) - 0.25m) < 0.02m)
                amountStr = $"{(int)Math.Floor(amount)} 1/4".Trim();
            else if (Math.Abs(amount - Math.Floor(amount) - 0.333m) < 0.02m)
                amountStr = $"{(int)Math.Floor(amount)} 1/3".Trim();
            else if (Math.Abs(amount - Math.Floor(amount) - 0.5m) < 0.02m)
                amountStr = $"{(int)Math.Floor(amount)} 1/2".Trim();
            else if (Math.Abs(amount - Math.Floor(amount) - 0.667m) < 0.02m)
                amountStr = $"{(int)Math.Floor(amount)} 2/3".Trim();
            else if (Math.Abs(amount - Math.Floor(amount) - 0.75m) < 0.02m)
                amountStr = $"{(int)Math.Floor(amount)} 3/4".Trim();
            else if (amount == Math.Floor(amount))
                amountStr = ((int)amount).ToString();
            else
                amountStr = amount.ToString("0.##");

            // Clean up "0 1/2" to just "1/2"
            if (amountStr.StartsWith("0 "))
                amountStr = amountStr.Substring(2);

            if (string.IsNullOrEmpty(unit))
                return amountStr;

            // Pluralize unit if needed
            var displayUnit = amount > 1 ? Pluralize(unit) : unit;
            return $"{amountStr} {displayUnit}";
        }

        private static string NormalizeUnit(string unit)
        {
            // First try exact match (for T vs t distinction)
            if (UnitAliases.TryGetValue(unit, out var normalized))
                return normalized;

            // Then try case-insensitive match for other units
            var lowerUnit = unit.ToLowerInvariant();
            if (UnitAliases.TryGetValue(lowerUnit, out var normalizedLower))
                return normalizedLower;

            return unit;
        }

        private static bool TryParseFraction(string fraction, out decimal result)
        {
            result = 0;
            var parts = fraction.Split('/');
            if (parts.Length != 2) return false;
            if (!decimal.TryParse(parts[0], out var num)) return false;
            if (!decimal.TryParse(parts[1], out var denom) || denom == 0) return false;
            result = num / denom;
            return true;
        }

        private static string Pluralize(string unit)
        {
            if (string.IsNullOrEmpty(unit)) return unit;

            // Already plural
            if (unit.EndsWith("s") && !unit.EndsWith("ss")) return unit;

            // Special cases
            return unit switch
            {
                "bunch" => "bunches",
                "inch" => "inches",
                "dash" => "dashes",
                "pinch" => "pinches",
                _ => unit + "s"
            };
        }
    }
}
