namespace Platerly.Api.DTOs
{
    // ── Request ────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/b2b/reports/patient-allergen-report
    /// Generates a white-label clinical PDF summarising a patient's allergen profile,
    /// safe/unsafe food lists, substitution recommendations, and optional nickel load
    /// assessment. Requires the "reports" API key scope (Professional tier).
    /// </summary>
    public class PatientAllergenReportRequest
    {
        /// <summary>Patient demographic information (displayed on the report cover).</summary>
        public PatientInfo Patient { get; set; } = new();

        /// <summary>Clinic / practitioner branding for the report header.</summary>
        public ClinicInfo Clinic { get; set; } = new();

        /// <summary>
        /// List of allergen type names the patient is confirmed allergic to.
        /// Accepted values: Peanuts, Tree Nuts, Sesame, Milk, Eggs, Fish,
        /// Shellfish, Gluten, Soy, Mustard, Sulfites, and any allergen type
        /// stored in GlobalIngredientAllergen.AllergenType.
        /// </summary>
        public List<string> AllergenProfile { get; set; } = new();

        /// <summary>
        /// Optional: include a nickel load section in the report.
        /// Accepts the same shape as POST /api/b2b/nickel/daily-load.
        /// When null the nickel section is omitted.
        /// </summary>
        public NickelDailyLoadRequest? NickelMealPlan { get; set; }

        /// <summary>
        /// Optional: maximum number of safe foods to list in the report. Default 20, max 50.
        /// </summary>
        public int SafeFoodLimit { get; set; } = 20;

        /// <summary>
        /// Optional: maximum number of unsafe foods to list. Default 15, max 30.
        /// </summary>
        public int UnsafeFoodLimit { get; set; } = 15;
    }

    public class PatientInfo
    {
        /// <summary>Patient full name (appears on the report).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional date of birth in ISO-8601 format (YYYY-MM-DD).</summary>
        public string? DateOfBirth { get; set; }

        /// <summary>Optional patient identifier / chart number.</summary>
        public string? PatientId { get; set; }

        /// <summary>Optional notes to include in the patient section.</summary>
        public string? ClinicalNotes { get; set; }
    }

    public class ClinicInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? PractitionerName { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }

        /// <summary>
        /// Optional clinic logo as a base64-encoded PNG or JPEG (without data URI prefix).
        /// Max 200 KB decoded. When provided, the logo is embedded in the PDF header.
        /// </summary>
        public string? LogoBase64 { get; set; }
    }

    // ── Response ───────────────────────────────────────────────────────────────

    public class PatientAllergenReportResult
    {
        /// <summary>Unique report identifier for reference/archival.</summary>
        public string ReportId { get; set; } = string.Empty;

        /// <summary>UTC timestamp the report was generated.</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The generated PDF file as a base64-encoded string.
        /// Decode to bytes and serve with Content-Type: application/pdf.
        /// </summary>
        public string PdfBase64 { get; set; } = string.Empty;

        /// <summary>Suggested filename for the download.</summary>
        public string Filename { get; set; } = string.Empty;

        /// <summary>PDF file size in bytes.</summary>
        public int FileSizeBytes { get; set; }

        /// <summary>Sections included in the report.</summary>
        public List<string> SectionsIncluded { get; set; } = new();

        // ── Report summary (mirrors the key PDF content) ──────────────────────

        public List<string> AllergenProfile { get; set; } = new();
        public int SafeFoodsCount { get; set; }
        public int UnsafeFoodsCount { get; set; }
        public int SubstitutionSuggestionsCount { get; set; }

        /// <summary>Nickel load summary if a meal plan was provided. Null otherwise.</summary>
        public NickelReportSummary? NickelSummary { get; set; }

        /// <summary>Unmatched allergens — names that were not found in the database.</summary>
        public List<string> UnknownAllergens { get; set; } = new();
    }

    /// <summary>
    /// Compact nickel summary embedded in the patient report and returned in the API response.
    /// </summary>
    public class NickelReportSummary
    {
        public decimal TotalNickelMcg { get; set; }
        public decimal ThresholdPercent { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
        public decimal RemainingBudgetMcg { get; set; }
    }
}
