using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>
    /// Seeds the base Allergy records into the database.
    /// These are the canonical allergen categories that users can select.
    /// Safe to run multiple times (idempotent).
    /// </summary>
    public sealed class AllergySeeder
    {
        private readonly PlaterlyDbContext _db;

        public AllergySeeder(PlaterlyDbContext db) => _db = db;

        /// <summary>
        /// Complete list of allergens to seed.
        /// Includes FDA Big 9 + EU 14 + additional common allergens.
        /// </summary>
        private static readonly string[] AllergenNames = new[]
        {
            // FDA Big 9 (US requirements)
            "Peanuts",
            "Tree Nuts",
            "Milk",
            "Eggs",
            "Fish",
            "Shellfish",
            "Soy",
            "Gluten",      // Wheat/Gluten
            "Sesame",

            // EU Additional (14 allergens required in EU)
            "Sulfites",
            "Mustard",
            "Celery",
            "Lupin",

            // Additional common allergens
            "Nickel",      // Systemic nickel allergy
        };

        public async Task SeedAsync(CancellationToken ct = default)
        {
            // Get existing allergen names (case-insensitive)
            var existingNames = await _db.Allergies
                .AsNoTracking()
                .Select(a => a.Name.ToLower())
                .ToListAsync(ct);

            var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

            // Add missing allergens
            var toAdd = new List<Models.Allergy>();

            foreach (var name in AllergenNames)
            {
                if (!existingSet.Contains(name.ToLower()))
                {
                    toAdd.Add(new Models.Allergy
                    {
                        Id = Guid.NewGuid(),
                        Name = name
                    });
                }
            }

            if (toAdd.Count > 0)
            {
                _db.Allergies.AddRange(toAdd);
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
