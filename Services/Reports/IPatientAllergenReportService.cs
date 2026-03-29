using Platerly.Api.DTOs;

namespace Platerly.Api.Services.Reports
{
    public interface IPatientAllergenReportService
    {
        Task<PatientAllergenReportResult> GenerateReportAsync(
            PatientAllergenReportRequest request,
            CancellationToken ct = default);
    }
}
