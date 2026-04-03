using System.Collections.Generic;

namespace Platerly.Api.Services
{
    /// <summary>Expands allergen names into ingredient keyword sets with optional severity filtering.</summary>
    public interface IAllergenResolver
    {
        /// <summary>
        /// Expands user allergy keywords (e.g., "nickel")
        /// into ingredient keywords (e.g., "chocolate", "cocoa", "oats"...).
        /// Returned set is lowercase, unique, non-empty.
        /// </summary>
        HashSet<string> ExpandKeywords(IEnumerable<string> userKeywords);

        /// <summary>
        /// Expands user allergy keywords with severity data.
        /// Example: ("chocolate", High), ("oats", Medium), ("milk", Low)
        /// </summary>
        List<(string term, AllergenResolver.Severity severity)> ExpandWithSeverity(IEnumerable<string> userKeywords);

        /// <summary>
        /// Filters expanded terms by minimum severity threshold ("low", "medium", "high").
        /// Returns list of ingredient terms meeting that level or higher.
        /// </summary>
        List<string> FilterByMinSeverity(IEnumerable<(string term, AllergenResolver.Severity severity)> terms, string minSeverity);

      

    }
}
