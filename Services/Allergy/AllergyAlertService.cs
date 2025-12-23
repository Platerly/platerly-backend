using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Services.Allergy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.Services.Allergy
{
    public class AllergyAlertService : IAllergyAlertService
    {
        private readonly IDbContextFactory<PlaterlyDbContext> _dbFactory;
        private readonly IAllergenResolver _resolver;

        public AllergyAlertService(
            IDbContextFactory<PlaterlyDbContext> dbFactory,
            IAllergenResolver resolver)
        {
            _dbFactory = dbFactory;
            _resolver = resolver;
        }

        public async Task<RecipeAlertsResponse> GetAlertsForRecipeAsync(Guid recipeId, Guid userId, CancellationToken ct)
        {
            // ✅ NEW CONTEXT PER CALL => no shared DbContext concurrency
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // --- load user's selected allergy roots (canonical) ---
            var userRoots = await (
                from ua in db.UserAllergies.AsNoTracking()
                join a in db.Allergies.AsNoTracking() on ua.AllergyId equals a.Id
                where ua.UserId == userId
                select a.Name
            ).ToListAsync(ct);

            userRoots = userRoots
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // --- load recipe ingredients (global names) ---
            var ingredients = await (
                from i in db.Ingredients.AsNoTracking()
                join g in db.GlobalIngredients.AsNoTracking() on i.GlobalIngredientId equals g.Id
                where i.RecipeId == recipeId
                select new IngredientAlertDetailDto
                {
                    IngredientName = g.Name,
                    
                }
            ).ToListAsync(ct);

            // If nothing selected or no ingredients, return empty
            if (userRoots.Count == 0 || ingredients.Count == 0)
            {
                return new RecipeAlertsResponse
                {
                    RecipeId = recipeId,
                    Alerts = new List<AllergyAlertDto>()
                };
            }

            // --- expand roots to terms+severity ---
            var expanded = _resolver.ExpandWithSeverity(userRoots);

            // --- compute matches (simple contains; improve later if needed) ---
            static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

            var alerts = new List<AllergyAlertDto>();

            foreach (var root in userRoots)
            {
                var rootNorm = Norm(root);

                // collect matching expanded terms for this root
                var termsForRoot = expanded
                    .Where(t => Norm(t.term).Contains(rootNorm) || rootNorm.Contains(Norm(t.term)))
                    .ToList();

                if (termsForRoot.Count == 0) continue;

                // ingredient hits
                var hits = new List<IngredientAlertDetailDto>();
                foreach (var ing in ingredients)
                {
                    var ingNorm = Norm(ing.IngredientName);
                    if (termsForRoot.Any(t => ingNorm.Contains(Norm(t.term))))
                        hits.Add(ing);
                }

                if (hits.Count == 0) continue;

                // max severity among matched terms
                var maxSev = termsForRoot.Max(t => t.severity).ToString().ToLowerInvariant();

                alerts.Add(new AllergyAlertDto
                {
                    AllergenType = root,     // canonical bucket name like "Milk", "Gluten", "Nickel"
                    HighestSeverity = maxSev,
                    Ingredients = hits
                });
            }

            return new RecipeAlertsResponse
            {
                RecipeId = recipeId,
                Alerts = alerts
            };
        }
    }
}
