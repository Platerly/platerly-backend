using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platerly.Api.DTOs;
using Platerly.Api.Middleware;
using Platerly.Api.Models;
using Platerly.Api.Services.Analytics;
using Platerly.Api.Services.Nutrition;
using Platerly.Api.Services.Safety;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2B Complete Recipe Analysis API.
    /// Combines allergen safety and nutrition into a single compound call.
    /// Applies the full Phase 2 safety layer: confidence scoring, disclaimer, audit logging,
    /// and three-tier alert hierarchy (FATAL / WARNING / INFO).
    /// Requires API Key authentication.
    ///
    /// NOTE: Sustainability module is not yet available and will be ignored if requested.
    /// </summary>
    [ApiController]
    [Route("api/b2b/recipe-analysis")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationOptions.DefaultScheme)]
    public class RecipeAnalysisController : B2BControllerBase
    {
        private readonly IRecipeSafetyService _safety;
        private readonly INutritionService _nutrition;
        private readonly ISafetyAuditService _audit;
        private readonly ILogger<RecipeAnalysisController> _logger;

        // Valid module names accepted by the Modules filter.
        // "sustainability" is intentionally excluded — dataset not yet available.
        private static readonly HashSet<string> ValidModules =
            new(StringComparer.OrdinalIgnoreCase) { "safety", "nutrition" };

        public RecipeAnalysisController(
            IRecipeSafetyService safety,
            INutritionService nutrition,
            ISafetyAuditService audit,
            ISafetyMetaService safetyMeta,
            ILogger<RecipeAnalysisController> logger)
            : base(safetyMeta)
        {
            _safety = safety;
            _nutrition = nutrition;
            _audit = audit;
            _logger = logger;
        }

        /// <summary>
        /// Run a complete allergen + nutrition analysis in a single call.
        ///
        /// Supply either:
        ///   - recipeId (Guid) — fetches a stored Recipzee recipe.
        ///   - ingredients (string[]) — ad-hoc list.
        ///
        /// Use the optional "modules" array (["safety", "nutrition"]) to restrict which analyses run.
        /// Note: "sustainability" is not yet available and will be ignored if included in modules.
        /// </summary>
        [HttpPost("complete")]
        public async Task<ActionResult> Complete(
            [FromBody] CompleteRecipeAnalysisRequest request,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            // ── 1. Validate ─────────────────────────────────────────────────────

            var hasRecipeId = request.RecipeId.HasValue && request.RecipeId.Value != Guid.Empty;
            var hasIngredients = request.Ingredients is { Count: > 0 };

            if (!hasRecipeId && !hasIngredients)
                return ApiError("Provide either a recipeId or an ingredients list.");

            if (hasRecipeId && hasIngredients)
                return ApiError("Provide recipeId OR ingredients — not both.");

            if (hasIngredients && request.Ingredients!.Count > 200)
                return ApiError("Maximum 200 ingredients per request.");

            request.Servings = Math.Clamp(request.Servings, 1, 100);

            // Resolve which modules to run
            var modules = request.Modules is { Count: > 0 }
                ? request.Modules
                    .Where(m => ValidModules.Contains(m))
                    .Select(m => m.ToLower())
                    .ToHashSet()
                : new HashSet<string>(ValidModules, StringComparer.OrdinalIgnoreCase);

            // Track if sustainability was requested so we can return an informational note
            bool sustainabilityRequested = request.Modules?.Any(m =>
                string.Equals(m, "sustainability", StringComparison.OrdinalIgnoreCase)) ?? false;

            var metadata = new AnalysisMetadata();
            var warnings = metadata.Warnings;

            // ── 2. Build ingredient list for safety + nutrition ──────────────────

            List<string> ingredientNames;

            if (hasRecipeId)
            {
                // For a known recipe, we'll pass the recipeId directly to the nutrition
                // and sustainability services — they resolve ingredients internally.
                // For the safety audit we still need a flat name list. We'll derive it
                // from the nutrition response after that call completes.
                ingredientNames = new List<string>();
            }
            else
            {
                ingredientNames = request.Ingredients!;
            }

            // ── 3. Execute modules in parallel ───────────────────────────────────

            RecipeSafetyAuditResponse? safetyResult = null;
            NutritionEstimateResponse? nutritionEstimate = null;
            RecipeNutritionResponse? nutritionRecipe = null;

            if (hasRecipeId)
            {
                var recipeId = request.RecipeId!.Value;

                var nutritionTask = modules.Contains("nutrition")
                    ? _nutrition.GetRecipeNutritionAsync(recipeId, ct)
                    : Task.FromResult<RecipeNutritionResponse?>(null);

                await nutritionTask;
                nutritionRecipe = await nutritionTask;

                // Extract ingredient names from nutrition result for the safety audit
                if (modules.Contains("safety"))
                {
                    if (nutritionRecipe != null)
                    {
                        ingredientNames = nutritionRecipe.Ingredients
                            .Select(i => i.IngredientName)
                            .ToList();
                    }

                    if (ingredientNames.Count > 0)
                    {
                        safetyResult = await _safety.AuditRecipeAsync(
                            new RecipeSafetyAuditRequest { Ingredients = ingredientNames }, ct);
                    }
                    else
                    {
                        warnings.Add("Safety audit skipped: could not resolve ingredient names for the given recipeId.");
                    }
                }
            }
            else
            {
                // Parallel: safety + nutrition (ingredient list mode)
                var safetyTask = modules.Contains("safety")
                    ? _safety.AuditRecipeAsync(
                        new RecipeSafetyAuditRequest { Ingredients = ingredientNames }, ct)
                    : Task.FromResult<RecipeSafetyAuditResponse?>(null)!;

                var nutritionTask = modules.Contains("nutrition")
                    ? _nutrition.EstimateNutritionAsync(
                        new NutritionEstimateRequest
                        {
                            Ingredients = ingredientNames
                                .Select(n => new NutritionIngredientInput { Name = n })
                                .ToList(),
                            Servings = request.Servings
                        }, ct)
                    : Task.FromResult<NutritionEstimateResponse?>(null)!;

                await Task.WhenAll(safetyTask, nutritionTask);
                safetyResult = await safetyTask;
                nutritionEstimate = await nutritionTask;
            }

            sw.Stop();

            // ── 4. Build metadata ────────────────────────────────────────────────

            metadata.AnalysisTimeMs = sw.ElapsedMilliseconds;
            metadata.IngredientCount = ingredientNames.Count > 0
                ? ingredientNames.Count
                : nutritionRecipe?.TotalIngredients ?? 0;

            if (safetyResult != null) metadata.ModulesExecuted.Add("safety");
            else if (modules.Contains("safety")) metadata.ModulesSkipped.Add("safety");

            if (nutritionEstimate != null || nutritionRecipe != null)
                metadata.ModulesExecuted.Add("nutrition");
            else if (modules.Contains("nutrition"))
                metadata.ModulesSkipped.Add("nutrition");

            if (sustainabilityRequested)
                metadata.ModulesSkipped.Add("sustainability: feature not yet available — coming Q3 2026");

            // ── 5. Build Phase 2 safety metadata ────────────────────────────────
            //
            // Use BuildForAllergyAlerts when we have real allergen data — this gives a
            // domain-specific confidence score. Fall back to BuildBaseline otherwise.

            SafetyMeta safetyMeta;

            if (safetyResult != null && safetyResult.AllergenFlags.Count > 0)
            {
                // Construct a RecipeAlertsResponse-shaped view from the safety audit
                // so the existing BuildForAllergyAlerts method can evaluate it.
                var alertsProxy = new RecipeAlertsResponse
                {
                    RecipeId = request.RecipeId ?? Guid.Empty,
                    Alerts = safetyResult.AllergenFlags
                        .GroupBy(f => f.FlagType)
                        .Select(g => new AllergyAlertDto
                        {
                            AllergenType = g.Key,
                            HighestSeverity = g.Max(f => f.Severity) ?? "Low",
                            Ingredients = g.Select(f => new IngredientAlertDetailDto
                            {
                                IngredientName = f.IngredientName,
                                SeverityResolved = f.Severity
                            }).ToList()
                        })
                        .ToList()
                };
                safetyMeta = _safetyMeta.BuildForAllergyAlerts(alertsProxy);
            }
            else
            {
                safetyMeta = _safetyMeta.BuildBaseline();
            }

            // Annotate nutritionRecipe warning if match rate was low
            if (nutritionRecipe != null && nutritionRecipe.TotalIngredients > 0)
            {
                int matchPct = nutritionRecipe.MatchedIngredients * 100 / nutritionRecipe.TotalIngredients;
                if (matchPct < 50)
                {
                    safetyMeta.Alerts.Add(new SafetyAlertDto
                    {
                        Severity = "WARNING",
                        Code = "LOW_NUTRITION_MATCH",
                        Message = $"Only {matchPct}% of ingredients matched USDA FoodData Central. " +
                                  "Nutrition estimates may be incomplete."
                    });
                }
            }

            if (sw.ElapsedMilliseconds > 2000)
            {
                safetyMeta.Alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "SLOW_ANALYSIS",
                    Message = $"Analysis took {sw.ElapsedMilliseconds}ms. " +
                              "Consider caching results for frequently analysed recipes."
                });
            }

            if (sustainabilityRequested)
            {
                safetyMeta.Alerts.Add(new SafetyAlertDto
                {
                    Severity = "INFO",
                    Code = "SUSTAINABILITY_NOT_YET_AVAILABLE",
                    Message = "Sustainability scoring is under development and was not included in this response. " +
                              "Expected availability: Q3 2026."
                });
            }

            // ── 6. Build response ────────────────────────────────────────────────

            // Normalise the two nutrition response shapes into one
            NutritionEstimateResponse? normalizedNutrition = nutritionEstimate;
            if (nutritionRecipe != null)
            {
                normalizedNutrition = new NutritionEstimateResponse
                {
                    Servings = nutritionRecipe.Servings,
                    PerServing = nutritionRecipe.PerServing,
                    Total = nutritionRecipe.Total,
                    Ingredients = nutritionRecipe.Ingredients,
                    MatchedIngredients = nutritionRecipe.MatchedIngredients,
                    TotalIngredients = nutritionRecipe.TotalIngredients
                };
            }

            var data = new CompleteRecipeAnalysisResponse
            {
                Title = request.Title
                    ?? nutritionRecipe?.RecipeTitle
                    ?? "Untitled Recipe",
                Safety = safetyResult,
                Nutrition = normalizedNutrition,
                Sustainability = null, // Not yet available — see metadata.ModulesSkipped
                Metadata = metadata
            };

            var response = new SafetyApiResponse<CompleteRecipeAnalysisResponse>
            {
                Data = data,
                Safety = safetyMeta
            };

            // ── 7. Audit log (fire-and-forget) ───────────────────────────────────

            var apiKeyId = GetApiKeyId();
            var highestSeverity = safetyMeta.Alerts.Count > 0
                ? (safetyMeta.Alerts.Any(a => a.Severity == "FATAL") ? "FATAL"
                    : safetyMeta.Alerts.Any(a => a.Severity == "WARNING") ? "WARNING"
                    : "INFO")
                : null;

            _ = _audit.LogAsync(new SafetyAuditLog
            {
                HttpMethod = "POST",
                Endpoint = "/api/b2b/recipe-analysis/complete",
                ApiKeyId = apiKeyId == Guid.Empty ? null : apiKeyId,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString().Length > 512
                    ? HttpContext.Request.Headers.UserAgent.ToString()[..512]
                    : HttpContext.Request.Headers.UserAgent.ToString(),
                ResponseStatusCode = 200,
                ResponseTimeMs = sw.ElapsedMilliseconds,
                AlertCount = safetyMeta.Alerts.Count,
                HighestAlertSeverity = highestSeverity,
                IngredientContext = string.Join(", ", ingredientNames.Take(20)),
                ConfidenceLevel = safetyMeta.Confidence.Level
            }, CancellationToken.None);

            return Ok(response);
        }
    }
}
