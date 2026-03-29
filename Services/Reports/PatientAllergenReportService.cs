using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using Platerly.Api.Services.Nickel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Platerly.Api.Services.Reports
{
    /// <summary>
    /// Generates a white-label clinical patient allergen report as a PDF.
    ///
    /// Report sections (in order):
    ///   1. Header       — clinic branding + logo
    ///   2. Patient Info — name, DOB, chart number, notes
    ///   3. Allergen Profile — confirmed allergens with clinical notes
    ///   4. Foods to Avoid — ingredients containing patient's allergens
    ///   5. Substitution Guide — safe alternatives for high-risk ingredients
    ///   6. Safe Foods   — verified allergen-free options
    ///   7. Nickel Load  — daily intake assessment (optional; only when NickelMealPlan provided)
    ///   8. Disclaimer   — mandatory clinical and data disclaimer
    /// </summary>
    public class PatientAllergenReportService : IPatientAllergenReportService
    {
        private readonly PlaterlyDbContext _db;
        private readonly INickelLoadService _nickel;
        private readonly ILogger<PatientAllergenReportService> _logger;

        // Clinical allergen descriptions for the allergen profile section
        private static readonly Dictionary<string, string> AllergenClinicalNotes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Peanuts"]   = "Avoid all peanuts and peanut-derived products. Check labels for 'groundnuts' or 'arachis oil'.",
                ["Tree Nuts"] = "Avoid almonds, cashews, walnuts, pecans, pistachios, hazelnuts, macadamia and Brazil nuts.",
                ["Sesame"]    = "Avoid sesame seeds, sesame oil, tahini, and hummus. May appear as 'til' or 'gingelly oil'.",
                ["Milk"]      = "Avoid all dairy: milk, cheese, butter, cream, yogurt, whey, casein, and lactalbumin.",
                ["Eggs"]      = "Avoid whole eggs, egg white, egg yolk, and egg-derived additives (albumin, ovalbumin, lysozyme).",
                ["Fish"]      = "Avoid all fin fish. Common sources: tuna, salmon, cod, tilapia, bass, halibut, anchovy.",
                ["Shellfish"] = "Avoid crustaceans (shrimp, crab, lobster) and molluscs (clams, mussels, oysters, scallops, squid).",
                ["Gluten"]    = "Avoid wheat, rye, barley, oats (unless certified gluten-free), triticale, spelt, and kamut.",
                ["Soy"]       = "Avoid soybeans, edamame, tofu, tempeh, miso, soy sauce, tamari, and textured soy protein.",
                ["Mustard"]   = "Avoid mustard seeds, mustard powder, mustard oil, and prepared mustard condiments.",
                ["Sulfites"]  = "Avoid sulfur dioxide (E220), sodium/potassium bisulfite, and metabisulfite (E221–E228). Common in wine, dried fruit, and pickled foods.",
            };

        public PatientAllergenReportService(
            PlaterlyDbContext db,
            INickelLoadService nickel,
            ILogger<PatientAllergenReportService> logger)
        {
            _db = db;
            _nickel = nickel;
            _logger = logger;
        }

        public async Task<PatientAllergenReportResult> GenerateReportAsync(
            PatientAllergenReportRequest request,
            CancellationToken ct = default)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var patientAllergens = request.AllergenProfile
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ── 1. Query foods to avoid ──────────────────────────────────────────

            int unsafeLimit = Math.Clamp(request.UnsafeFoodLimit, 1, 30);

            var unsafeFoods = await _db.GlobalIngredients
                .AsNoTracking()
                .Include(gi => gi.Allergens)
                .Where(gi => gi.IsActive &&
                             gi.Allergens.Any(a =>
                                 patientAllergens.Contains(a.AllergenType) &&
                                 !a.MayContain &&
                                 !a.ProcessingAid))
                .OrderByDescending(gi => gi.TotalUsageCount)
                .Take(unsafeLimit)
                .ToListAsync(ct);

            // ── 2. Query safe foods ──────────────────────────────────────────────

            int safeLimit = Math.Clamp(request.SafeFoodLimit, 1, 50);

            var safeFoods = await _db.GlobalIngredients
                .AsNoTracking()
                .Where(gi => gi.IsActive &&
                             gi.IsVerified &&
                             gi.Allergens.Any() &&
                             !gi.Allergens.Any(a =>
                                 patientAllergens.Contains(a.AllergenType)))
                .OrderByDescending(gi => gi.TotalUsageCount)
                .Take(safeLimit)
                .ToListAsync(ct);

            // ── 3. Query substitutions for unsafe foods ──────────────────────────

            var unsafeIds = unsafeFoods.Select(u => u.Id).ToList();

            var substitutionRows = await _db.IngredientSubstitutions
                .AsNoTracking()
                .Include(s => s.SubstituteIngredient)
                    .ThenInclude(si => si.Allergens)
                .Where(s => unsafeIds.Contains(s.OriginalIngredientId) &&
                            s.IsActive &&
                            s.Reason == SubstitutionReason.Allergy)
                .OrderByDescending(s => s.ConfidenceScore)
                .ToListAsync(ct);

            // Only include substitutes that don't contain patient's allergens
            var safeSubs = substitutionRows
                .Where(s => !s.SubstituteIngredient.Allergens.Any(a =>
                    patientAllergens.Contains(a.AllergenType) && !a.MayContain))
                .ToList();

            // Group by original ingredient (top 2 subs each)
            var subsByOriginal = safeSubs
                .GroupBy(s => s.OriginalIngredientId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Take(2).ToList());

            // ── 4. Optional nickel load calculation ─────────────────────────────

            NickelDailyLoadResponse? nickelResult = null;
            if (request.NickelMealPlan != null)
            {
                try
                {
                    nickelResult = await _nickel.CalculateDailyLoadAsync(
                        request.NickelMealPlan, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nickel load calculation failed for patient report; section will be omitted.");
                }
            }

            // ── 5. Identify unknown allergens ────────────────────────────────────

            var allKnownTypes = await _db.GlobalIngredientAllergens
                .AsNoTracking()
                .Select(a => a.AllergenType)
                .Distinct()
                .ToListAsync(ct);

            var unknownAllergens = patientAllergens
                .Where(a => !allKnownTypes.Any(k =>
                    string.Equals(k, a, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // ── 6. Generate PDF ──────────────────────────────────────────────────

            var reportId = Guid.NewGuid().ToString("N")[..12].ToUpper();
            var reportDate = DateTime.UtcNow;

            byte[] logoBytes = Array.Empty<byte>();
            if (!string.IsNullOrWhiteSpace(request.Clinic.LogoBase64))
            {
                try
                {
                    logoBytes = Convert.FromBase64String(request.Clinic.LogoBase64);
                    if (logoBytes.Length > 204800) // 200 KB max
                        logoBytes = Array.Empty<byte>();
                }
                catch
                {
                    logoBytes = Array.Empty<byte>();
                }
            }

            var sections = BuildSectionList(nickelResult, safeFoods, unsafeFoods, safeSubs);

            var pdfBytes = GeneratePdf(
                request, patientAllergens, unsafeFoods, safeFoods,
                subsByOriginal, nickelResult, unknownAllergens,
                logoBytes, reportId, reportDate);

            // ── 7. Build result ──────────────────────────────────────────────────

            NickelReportSummary? nickelSummary = nickelResult == null ? null : new NickelReportSummary
            {
                TotalNickelMcg    = nickelResult.TotalNickelMcg,
                ThresholdPercent  = nickelResult.ThresholdPercent,
                RiskLevel         = nickelResult.RiskLevel,
                RemainingBudgetMcg = nickelResult.RemainingBudgetMcg
            };

            return new PatientAllergenReportResult
            {
                ReportId      = reportId,
                GeneratedAt   = reportDate,
                PdfBase64     = Convert.ToBase64String(pdfBytes),
                Filename      = $"patient-allergen-report-{reportDate:yyyy-MM-dd}-{reportId}.pdf",
                FileSizeBytes = pdfBytes.Length,
                SectionsIncluded  = sections,
                AllergenProfile   = patientAllergens,
                SafeFoodsCount    = safeFoods.Count,
                UnsafeFoodsCount  = unsafeFoods.Count,
                SubstitutionSuggestionsCount = safeSubs.Count,
                NickelSummary     = nickelSummary,
                UnknownAllergens  = unknownAllergens
            };
        }

        // ── PDF Generation ─────────────────────────────────────────────────────

        private static byte[] GeneratePdf(
            PatientAllergenReportRequest request,
            List<string> allergens,
            List<GlobalIngredient> unsafeFoods,
            List<GlobalIngredient> safeFoods,
            Dictionary<Guid, List<IngredientSubstitution>> subsByOriginal,
            NickelDailyLoadResponse? nickelResult,
            List<string> unknownAllergens,
            byte[] logoBytes,
            string reportId,
            DateTime reportDate)
        {
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginVertical(1.5f, Unit.Centimetre);
                    page.MarginHorizontal(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    // ─── HEADER ────────────────────────────────────────────────
                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            // Logo slot
                            if (logoBytes.Length > 0)
                            {
                                row.ConstantItem(64).Height(64)
                                    .Image(logoBytes)
                                    .FitArea();
                                row.ConstantItem(12); // spacer
                            }

                            // Clinic details
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(request.Clinic.Name)
                                    .Bold().FontSize(14);
                                if (!string.IsNullOrWhiteSpace(request.Clinic.PractitionerName))
                                    c.Item().Text(request.Clinic.PractitionerName).FontSize(10);
                                if (!string.IsNullOrWhiteSpace(request.Clinic.Address))
                                    c.Item().Text(request.Clinic.Address).FontSize(9).FontColor(Colors.Grey.Darken1);
                                if (!string.IsNullOrWhiteSpace(request.Clinic.Phone))
                                    c.Item().Text(request.Clinic.Phone).FontSize(9).FontColor(Colors.Grey.Darken1);
                            });

                            // Report meta (right-aligned)
                            row.ConstantItem(120).AlignRight().Column(c =>
                            {
                                c.Item().Text("ALLERGEN REPORT").Bold().FontSize(11).FontColor(Colors.Blue.Darken2);
                                c.Item().Text($"Date: {reportDate:yyyy-MM-dd}").FontSize(9);
                                c.Item().Text($"Report ID: {reportId}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });

                        col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(Colors.Blue.Darken2);
                        col.Item().PaddingVertical(4);
                    });

                    // ─── FOOTER ────────────────────────────────────────────────
                    page.Footer().Column(col =>
                    {
                        col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                        col.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().Text(
                                "This report is generated for clinical reference only. " +
                                "Allergen data is derived from the Platerly/Recipzee allergen database. " +
                                "Verify all dietary recommendations with the patient's care team before implementation.")
                                .FontSize(7).FontColor(Colors.Grey.Darken1).Italic();
                            row.ConstantItem(50).AlignRight()
                                .Text(x =>
                                {
                                    x.Span("Page ").FontSize(8);
                                    x.CurrentPageNumber().FontSize(8);
                                    x.Span(" / ").FontSize(8);
                                    x.TotalPages().FontSize(8);
                                });
                        });
                    });

                    // ─── CONTENT ───────────────────────────────────────────────
                    page.Content().Column(col =>
                    {
                        // ── Patient Info ──────────────────────────────────────
                        col.Item().Background(Colors.Blue.Lighten5).Padding(10).Column(c =>
                        {
                            c.Item().Text("PATIENT INFORMATION").Bold().FontSize(10).FontColor(Colors.Blue.Darken2);
                            c.Item().PaddingTop(4).Row(row =>
                            {
                                row.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Text($"Patient: {request.Patient.Name}").Bold();
                                    if (!string.IsNullOrWhiteSpace(request.Patient.DateOfBirth))
                                        inner.Item().Text($"Date of Birth: {request.Patient.DateOfBirth}");
                                    if (!string.IsNullOrWhiteSpace(request.Patient.PatientId))
                                        inner.Item().Text($"Chart #: {request.Patient.PatientId}");
                                });
                                row.ConstantItem(150).Column(inner =>
                                {
                                    inner.Item().Text($"Report Date: {reportDate:MMMM d, yyyy}");
                                    inner.Item().Text($"Allergens on file: {allergens.Count}");
                                });
                            });
                            if (!string.IsNullOrWhiteSpace(request.Patient.ClinicalNotes))
                            {
                                col.Item().PaddingTop(4).Text($"Clinical notes: {request.Patient.ClinicalNotes}")
                                    .FontSize(9).Italic();
                            }
                        });

                        col.Item().PaddingVertical(8);

                        // ── Allergen Profile ──────────────────────────────────
                        col.Item().Text("CONFIRMED ALLERGEN PROFILE").Bold().FontSize(11)
                            .FontColor(Colors.Red.Darken2);
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(130);
                                cols.RelativeColumn();
                            });

                            // Header row
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Red.Lighten4).Padding(5)
                                    .Text("Allergen").Bold().FontSize(9);
                                header.Cell().Background(Colors.Red.Lighten4).Padding(5)
                                    .Text("Clinical Guidance").Bold().FontSize(9);
                            });

                            bool alt = false;
                            foreach (var allergen in allergens)
                            {
                                var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                                table.Cell().Background(bg).Padding(5).Text(allergen).Bold().FontSize(9);
                                var note = AllergenClinicalNotes.TryGetValue(allergen, out var n)
                                    ? n
                                    : "Avoid all products containing this allergen. Review ingredient labels carefully.";
                                table.Cell().Background(bg).Padding(5).Text(note).FontSize(9);
                                alt = !alt;
                            }
                        });

                        if (unknownAllergens.Any())
                        {
                            col.Item().PaddingTop(4).Text(
                                $"⚠ Unrecognised allergens (not in database): {string.Join(", ", unknownAllergens)}")
                                .FontSize(8).FontColor(Colors.Orange.Darken2).Italic();
                        }

                        col.Item().PaddingVertical(10);

                        // ── Foods to Avoid ────────────────────────────────────
                        if (unsafeFoods.Any())
                        {
                            col.Item().Text("FOODS TO AVOID").Bold().FontSize(11)
                                .FontColor(Colors.Red.Darken2);
                            col.Item().PaddingTop(2).Text(
                                "The following common ingredients are confirmed to contain your patient's allergens.")
                                .FontSize(9).Italic().FontColor(Colors.Grey.Darken1);
                            col.Item().PaddingTop(6).Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(150);
                                    cols.RelativeColumn();
                                    cols.ConstantColumn(130);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Red.Lighten4).Padding(5)
                                        .Text("Ingredient").Bold().FontSize(9);
                                    header.Cell().Background(Colors.Red.Lighten4).Padding(5)
                                        .Text("Triggers").Bold().FontSize(9);
                                    header.Cell().Background(Colors.Red.Lighten4).Padding(5)
                                        .Text("Safe Substitute").Bold().FontSize(9);
                                });

                                bool alt = false;
                                foreach (var food in unsafeFoods)
                                {
                                    var bg = alt ? Colors.Red.Lighten5 : Colors.White;
                                    var triggers = food.Allergens
                                        .Where(a => allergens.Contains(a.AllergenType, StringComparer.OrdinalIgnoreCase)
                                                    && !a.MayContain)
                                        .Select(a => a.AllergenType)
                                        .Distinct()
                                        .ToList();

                                    string substituteText = "—";
                                    if (subsByOriginal.TryGetValue(food.Id, out var subs) && subs.Any())
                                        substituteText = string.Join(", ", subs.Select(s => s.SubstituteIngredient.Name));

                                    table.Cell().Background(bg).Padding(5).Text(food.Name).Bold().FontSize(9);
                                    table.Cell().Background(bg).Padding(5)
                                        .Text(string.Join(", ", triggers)).FontSize(9).FontColor(Colors.Red.Darken1);
                                    table.Cell().Background(bg).Padding(5)
                                        .Text(substituteText).FontSize(9).FontColor(Colors.Green.Darken2);
                                    alt = !alt;
                                }
                            });

                            col.Item().PaddingVertical(10);
                        }

                        // ── Safe Foods ────────────────────────────────────────
                        if (safeFoods.Any())
                        {
                            col.Item().Text("GENERALLY SAFE OPTIONS").Bold().FontSize(11)
                                .FontColor(Colors.Green.Darken2);
                            col.Item().PaddingTop(2).Text(
                                "These ingredients have been verified allergen-free for your patient's confirmed allergens.")
                                .FontSize(9).Italic().FontColor(Colors.Grey.Darken1);

                            col.Item().PaddingTop(6);

                            // Render as a wrap-friendly tag cloud using a grid table
                            int cols4 = 4;
                            var rows4 = (int)Math.Ceiling(safeFoods.Count / (double)cols4);
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cd =>
                                {
                                    for (int i = 0; i < cols4; i++)
                                        cd.RelativeColumn();
                                });

                                for (int r = 0; r < rows4; r++)
                                {
                                    for (int c = 0; c < cols4; c++)
                                    {
                                        int idx = r * cols4 + c;
                                        if (idx < safeFoods.Count)
                                        {
                                            table.Cell()
                                                .Background(Colors.Green.Lighten5)
                                                .Border(0.5f).BorderColor(Colors.Green.Lighten3)
                                                .Padding(5)
                                                .Text(safeFoods[idx].Name)
                                                .FontSize(9).FontColor(Colors.Green.Darken3);
                                        }
                                        else
                                        {
                                            table.Cell(); // empty cell
                                        }
                                    }
                                }
                            });

                            col.Item().PaddingTop(4).Text(
                                "✓ These options are drawn from verified allergen records. " +
                                "Always confirm with ingredient labels — formulations change.")
                                .FontSize(8).FontColor(Colors.Green.Darken2).Italic();

                            col.Item().PaddingVertical(10);
                        }

                        // ── Nickel Load ───────────────────────────────────────
                        if (nickelResult != null)
                        {
                            col.Item().Text("NICKEL LOAD ASSESSMENT").Bold().FontSize(11)
                                .FontColor(Colors.Purple.Darken2);
                            col.Item().PaddingTop(2).Text(
                                $"Daily nickel reference threshold: {NickelThresholds.DailyThresholdMcg} μg/day (Mislankar & Zirwas 2013).")
                                .FontSize(9).Italic().FontColor(Colors.Grey.Darken1);

                            col.Item().PaddingTop(6).Row(row =>
                            {
                                var riskColor = nickelResult.RiskLevel == "EXCEEDED"
                                    ? Colors.Red.Darken1
                                    : nickelResult.RiskLevel == "MODERATE"
                                        ? Colors.Orange.Darken1
                                        : Colors.Green.Darken2;

                                row.ConstantItem(160).Background(Colors.Purple.Lighten5).Padding(8).Column(c =>
                                {
                                    c.Item().Text("Total Daily Nickel").FontSize(9).FontColor(Colors.Grey.Darken1);
                                    c.Item().Text($"{nickelResult.TotalNickelMcg} μg").Bold().FontSize(14);
                                    c.Item().Text($"{nickelResult.ThresholdPercent}% of threshold").FontSize(9);
                                });
                                row.ConstantItem(10); // spacer
                                row.ConstantItem(120).Background(Colors.Purple.Lighten5).Padding(8).Column(c =>
                                {
                                    c.Item().Text("Risk Level").FontSize(9).FontColor(Colors.Grey.Darken1);
                                    c.Item().Text(nickelResult.RiskLevel).Bold().FontSize(14).FontColor(riskColor);
                                    c.Item().Text($"Budget remaining: {nickelResult.RemainingBudgetMcg} μg").FontSize(9);
                                });
                            });

                            if (nickelResult.TopContributors.Any())
                            {
                                col.Item().PaddingTop(8).Text("Top Nickel Contributors").Bold().FontSize(10);
                                col.Item().PaddingTop(4).Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.RelativeColumn();
                                        cols.ConstantColumn(90);
                                        cols.ConstantColumn(90);
                                        cols.ConstantColumn(80);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Background(Colors.Purple.Lighten4).Padding(5)
                                            .Text("Ingredient").Bold().FontSize(9);
                                        header.Cell().Background(Colors.Purple.Lighten4).Padding(5)
                                            .Text("Meal").Bold().FontSize(9);
                                        header.Cell().Background(Colors.Purple.Lighten4).Padding(5)
                                            .Text("Nickel (μg)").Bold().FontSize(9);
                                        header.Cell().Background(Colors.Purple.Lighten4).Padding(5)
                                            .Text("% Threshold").Bold().FontSize(9);
                                    });

                                    bool alt = false;
                                    foreach (var contrib in nickelResult.TopContributors.Take(10))
                                    {
                                        var bg = alt ? Colors.Purple.Lighten5 : Colors.White;
                                        table.Cell().Background(bg).Padding(5).Text(contrib.Name).FontSize(9);
                                        table.Cell().Background(bg).Padding(5).Text(contrib.MealLabel).FontSize(9);
                                        table.Cell().Background(bg).Padding(5).Text($"{contrib.NickelContributionMcg}").FontSize(9);
                                        table.Cell().Background(bg).Padding(5).Text($"{contrib.ContributionPercent}%").FontSize(9);
                                        alt = !alt;
                                    }
                                });
                            }

                            col.Item().PaddingTop(6).Text(
                                "Nickel data: Rebelytics R&D Inc. v9.1.1, September 2025 — 257 studies, 20,575 measurements.")
                                .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);

                            col.Item().PaddingVertical(10);
                        }

                        // ── Mandatory Disclaimer ──────────────────────────────
                        col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
                        {
                            c.Item().Text("IMPORTANT DISCLAIMER").Bold().FontSize(9);
                            c.Item().PaddingTop(4).Text(
                                "This report is generated by the Platerly/Recipzee B2B API for clinical reference purposes only. " +
                                "Allergen data is derived from the Platerly allergen database using ingredient name matching — " +
                                "it does not account for processing aids, carry-over allergens, or undeclared ingredients. " +
                                "Food formulations change; always verify allergen declarations on current packaging. " +
                                "This report does not constitute medical advice. Dietary recommendations should be validated " +
                                "by a qualified healthcare professional before implementation. " +
                                "Recipzee / Platerly Inc. accepts no liability for adverse reactions arising from reliance on this report.")
                                .FontSize(8).Italic().FontColor(Colors.Grey.Darken2);
                        });
                    });
                });
            });

            return doc.GeneratePdf();
        }

        private static List<string> BuildSectionList(
            NickelDailyLoadResponse? nickelResult,
            List<GlobalIngredient> safeFoods,
            List<GlobalIngredient> unsafeFoods,
            List<IngredientSubstitution> subs)
        {
            var sections = new List<string> { "patientInfo", "allergenProfile" };
            if (unsafeFoods.Any()) sections.Add("foodsToAvoid");
            if (subs.Any())        sections.Add("substitutionGuide");
            if (safeFoods.Any())   sections.Add("safeFoods");
            if (nickelResult != null) sections.Add("nickelLoad");
            sections.Add("disclaimer");
            return sections;
        }
    }
}
