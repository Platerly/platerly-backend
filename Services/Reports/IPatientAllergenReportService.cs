using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Reports
{
    /// <summary>Generates white-label clinical patient allergen reports as QuestPDF PDFs with foods-to-avoid, substitutions, safe foods, and optional nickel load sections.</summary>
    public interface IPatientAllergenReportService
    {
        Task<PatientAllergenReportResult> GenerateReportAsync(
            PatientAllergenReportRequest request,
            CancellationToken ct = default);
    }
}
