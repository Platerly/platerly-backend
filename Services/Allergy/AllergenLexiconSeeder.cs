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
    /// Seeds a curated allergen keyword lexicon and links each keyword to its canonical Allergy.
    /// Safe to run multiple times (idempotent).
    /// </summary>
    public sealed class AllergyLexiconSeeder
    {
        private readonly PlaterlyDbContext _db;

        public AllergyLexiconSeeder(PlaterlyDbContext db) => _db = db;

        private sealed class Row
        {
            public AllergenKeywordKind Kind { get; init; }
            public string Text { get; init; } = "";
            public string? ENumber { get; init; }
            public string? Notes { get; init; }
        }

        public async Task SeedAsync(CancellationToken ct = default)
        {
            // ------------------------------------------------------------
            // Canonical allergy name -> keyword rows
            // ------------------------------------------------------------
            var lexicon = new Dictionary<string, Row[]>(StringComparer.OrdinalIgnoreCase)
            {
                // --------------------------------------------------------
                // NICKEL (trace-heavy foods)
                // --------------------------------------------------------
                ["Nickel"] = new[]
                {
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "cocoa",     Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "chocolate", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "beans",     Notes = "Legumes often high in nickel" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "lentils" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "peas" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "oats" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "legumes",   Notes = "May indicate nickel risk" },
                },

                // --------------------------------------------------------
                // SULFITES (E220–E228 + common synonyms)
                // --------------------------------------------------------
                ["Sulfites"] = new[]
                {
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E220", ENumber = "E220", Notes = "Sulfur dioxide" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E221", ENumber = "E221", Notes = "Sodium sulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E222", ENumber = "E222", Notes = "Sodium bisulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E223", ENumber = "E223", Notes = "Sodium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E224", ENumber = "E224", Notes = "Potassium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E226", ENumber = "E226", Notes = "Calcium sulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E227", ENumber = "E227", Notes = "Calcium hydrogen sulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E228", ENumber = "E228", Notes = "Potassium hydrogen sulfite" },

                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sodium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "potassium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulphites", Notes = "UK spelling" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulfur dioxide" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulphite" },
                },

                // --------------------------------------------------------
                // GLUTEN / CELIAC
                // --------------------------------------------------------
                ["Gluten"] = new[]
                {
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "wheat" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "barley" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "rye" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "triticale" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "spelt" },
                    new Row { Kind = AllergenKeywordKind.Food,     Text = "semolina" },
                    new Row { Kind = AllergenKeywordKind.Synonym,  Text = "malt", Notes = "Usually derived from barley" },
                    new Row { Kind = AllergenKeywordKind.Synonym,  Text = "gluten" },
                },

                // --------------------------------------------------------
                // MILK / DAIRY
                // --------------------------------------------------------
                ["Milk"] = new[]
                {
                    new Row { Kind = AllergenKeywordKind.Synonym,  Text = "whey",    Notes = "Milk protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym,  Text = "casein",  Notes = "Milk protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym,  Text = "lactose", Notes = "Milk sugar" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "dairy" },
                },
            };

            // ------------------------------------------------------------
            // Load Allergy ids by name (Guid)
            // ------------------------------------------------------------
            var allergies = await _db.Allergies
                .AsNoTracking()
                .Select(a => new { a.Id, a.Name })
                .ToListAsync(ct);

            var allergyIdByName = allergies.ToDictionary(a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

            // Preload all existing keywords once to reduce roundtrips
            var existingKeywords = await _db.AllergenKeywords
                .AsNoTracking()
                .Select(k => new { k.Id, k.Text, k.Kind })
                .ToListAsync(ct);

            // Case-insensitive lookup using normalized (lower) text:
            // (Kind, lowerText) -> keywordId
            var keywordIdByKey = new Dictionary<(AllergenKeywordKind Kind, string LowerText), int>();
            foreach (var k in existingKeywords)
            {
                var lower = k.Text?.ToLowerInvariant() ?? string.Empty;
                var key = (k.Kind, lower);
                if (!keywordIdByKey.ContainsKey(key))
                    keywordIdByKey[key] = k.Id;
            }

            // We’ll collect new entities and save in batches
            var toInsertKeywords = new List<AllergenKeyword>();
            var toInsertLinks = new List<AllergenKeywordAllergy>();

            foreach (var kvp in lexicon)
            {
                var allergyName = kvp.Key;
                var rows = kvp.Value;

                if (!allergyIdByName.TryGetValue(allergyName, out Guid allergyId))
                    continue; // skip if the canonical Allergy record is not present

                // Ensure keywords exist
                foreach (var r in rows)
                {
                    var key = (r.Kind, (r.Text ?? string.Empty).ToLowerInvariant());

                    if (!keywordIdByKey.TryGetValue(key, out _))
                    {
                        // create new keyword
                        var k = new AllergenKeyword
                        {
                            Text = r.Text,
                            Kind = r.Kind,
                            ENumber = r.ENumber,
                            Notes = r.Notes
                        };
                        toInsertKeywords.Add(k);
                    }
                }

                // Flush new keywords for this allergy bucket so we have their IDs
                if (toInsertKeywords.Count > 0)
                {
                    _db.AllergenKeywords.AddRange(toInsertKeywords);
                    await _db.SaveChangesAsync(ct);

                    // refresh cache with newly created ids
                    foreach (var k in toInsertKeywords)
                    {
                        var lower = k.Text?.ToLowerInvariant() ?? string.Empty;
                        keywordIdByKey[(k.Kind, lower)] = k.Id;
                    }

                    toInsertKeywords.Clear();
                }

                // Now ensure link rows exist
                foreach (var r in rows)
                {
                    var key = (r.Kind, (r.Text ?? string.Empty).ToLowerInvariant());
                    if (!keywordIdByKey.TryGetValue(key, out int kwId))
                        continue;

                    bool linkExists = await _db.AllergenKeywordAllergies
                        .AsNoTracking()
                        .AnyAsync(x => x.AllergenKeywordId == kwId && x.AllergyId == allergyId, ct);

                    if (!linkExists)
                    {
                        toInsertLinks.Add(new AllergenKeywordAllergy
                        {
                            AllergenKeywordId = kwId,
                            AllergyId = allergyId
                        });
                    }
                }
            }

            if (toInsertLinks.Count > 0)
            {
                _db.AllergenKeywordAllergies.AddRange(toInsertLinks);
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
