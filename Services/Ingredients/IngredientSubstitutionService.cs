using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Ingredients
{
    public class IngredientSubstitutionService : IIngredientSubstitutionService
    {
        private readonly PlaterlyDbContext _db;
        private readonly ILogger<IngredientSubstitutionService> _logger;

        public IngredientSubstitutionService(
            PlaterlyDbContext db,
            ILogger<IngredientSubstitutionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<SubstitutionDto>> GetSubstitutionsForIngredientAsync(
            Guid ingredientId,
            CancellationToken ct = default)
        {
            var substitutions = await _db.IngredientSubstitutions
                .Where(s => s.OriginalIngredientId == ingredientId)
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Include(s => s.SuggestedByUser)
                .OrderByDescending(s => s.ConfidenceScore)
                .ThenByDescending(s => s.UsageCount)
                .AsNoTracking()
                .ToListAsync(ct);

            return substitutions.Select(MapToDto).ToList();
        }

        public async Task<List<SubstitutionDto>> GetSubstitutionsByAllergyAsync(
            Guid allergyId,
            CancellationToken ct = default)
        {
            var substitutions = await _db.IngredientSubstitutions
                .Where(s => s.AllergyId == allergyId)
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Include(s => s.SuggestedByUser)
                .OrderByDescending(s => s.ConfidenceScore)
                .ThenByDescending(s => s.UsageCount)
                .AsNoTracking()
                .ToListAsync(ct);

            return substitutions.Select(MapToDto).ToList();
        }

        public async Task<List<SubstitutionDto>> GetSubstitutionsByAllergyNameAsync(
            string allergyName,
            CancellationToken ct = default)
        {
            var normalizedName = allergyName.ToLowerInvariant().Trim();

            // Find the allergy by name
            var allergy = await _db.Allergies
                .Where(a => a.Name.ToLower() == normalizedName)
                .FirstOrDefaultAsync(ct);

            if (allergy == null)
                return new List<SubstitutionDto>();

            return await GetSubstitutionsByAllergyAsync(allergy.Id, ct);
        }

        public async Task<List<SubstitutionDto>> GetSubstitutionsByReasonAsync(
            string reason,
            CancellationToken ct = default)
        {
            if (!Enum.TryParse<SubstitutionReason>(reason, true, out var parsedReason))
            {
                return new List<SubstitutionDto>();
            }

            var substitutions = await _db.IngredientSubstitutions
                .Where(s => s.Reason == parsedReason)
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Include(s => s.SuggestedByUser)
                .OrderByDescending(s => s.ConfidenceScore)
                .ThenByDescending(s => s.UsageCount)
                .AsNoTracking()
                .ToListAsync(ct);

            return substitutions.Select(MapToDto).ToList();
        }

        public async Task<List<SubstitutionDto>> GetSubstitutionsForIngredientNameAsync(
            string ingredientName,
            List<Guid>? userAllergyIds = null,
            CancellationToken ct = default)
        {
            var normalizedName = ingredientName.ToLowerInvariant().Trim();

            // 1. Exact match
            var ingredient = await _db.GlobalIngredients
                .Where(g => g.NormalizedName == normalizedName ||
                            (g.Aliases != null && g.Aliases.ToLower().Contains(normalizedName)))
                .FirstOrDefaultAsync(ct);

            // 2. Partial match fallback — handles "Chicken Breast" → "Chicken", "Unsalted Butter" → "Butter"
            // Load only names that have substitutions (small set ~74 entries) and match in memory
            if (ingredient == null)
            {
                var candidateNames = await _db.GlobalIngredients
                    .Where(g => g.SubstitutionsAsOriginal.Any())
                    .Select(g => new { g.Id, g.NormalizedName })
                    .ToListAsync(ct);

                var best = candidateNames
                    .Where(g => normalizedName.Contains(g.NormalizedName) || g.NormalizedName.Contains(normalizedName))
                    .OrderByDescending(g => g.NormalizedName.Length)
                    .FirstOrDefault();

                if (best != null)
                    ingredient = await _db.GlobalIngredients.FindAsync(new object[] { best.Id }, ct);
            }

            if (ingredient == null)
                return new List<SubstitutionDto>();

            var query = _db.IngredientSubstitutions
                .Where(s => s.OriginalIngredientId == ingredient.Id);

            // If user has allergies, prioritize substitutions for those allergies
            if (userAllergyIds != null && userAllergyIds.Any())
            {
                query = query.Where(s => s.AllergyId == null || userAllergyIds.Contains(s.AllergyId.Value));
            }

            var substitutions = await query
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Include(s => s.SuggestedByUser)
                .OrderByDescending(s => s.ConfidenceScore)
                .ThenByDescending(s => s.UsageCount)
                .AsNoTracking()
                .ToListAsync(ct);

            return substitutions.Select(MapToDto).ToList();
        }

        public async Task<SubstitutionDto> SuggestSubstitutionAsync(
            SuggestSubstitutionDto dto,
            Guid userId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.OriginalIngredientName) ||
                string.IsNullOrWhiteSpace(dto.SubstituteIngredientName))
            {
                throw new InvalidOperationException("Both ingredient names are required");
            }

            // Resolve or create GlobalIngredient records by name
            var originalIngredient = await ResolveOrCreateIngredientAsync(dto.OriginalIngredientName, ct);
            var substituteIngredient = await ResolveOrCreateIngredientAsync(dto.SubstituteIngredientName, ct);

            // Check if this substitution already exists
            var existing = await _db.IngredientSubstitutions
                .FirstOrDefaultAsync(s =>
                    s.OriginalIngredientId == originalIngredient.Id &&
                    s.SubstituteIngredientId == substituteIngredient.Id,
                    ct);

            if (existing != null)
            {
                throw new InvalidOperationException("This substitution already exists");
            }

            // Parse reason
            if (!Enum.TryParse<SubstitutionReason>(dto.Reason, true, out var reason))
            {
                reason = SubstitutionReason.Other;
            }

            var substitution = new IngredientSubstitution
            {
                Id = Guid.NewGuid(),
                OriginalIngredientId = originalIngredient.Id,
                SubstituteIngredientId = substituteIngredient.Id,
                Reason = reason,
                ConversionRatio = dto.ConversionRatio,
                Notes = dto.Notes,
                SuggestedByUserId = userId,
                ConfidenceScore = 50, // Start at neutral
                PositiveVotes = 0,
                NegativeVotes = 0,
                UsageCount = 0,
                IsVerified = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.IngredientSubstitutions.Add(substitution);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "User {UserId} suggested substitution: {Original} -> {Substitute}",
                userId, dto.OriginalIngredientName, dto.SubstituteIngredientName);

            // Reload with includes
            var created = await _db.IngredientSubstitutions
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Include(s => s.SuggestedByUser)
                .FirstAsync(s => s.Id == substitution.Id, ct);

            return MapToDto(created);
        }

        public async Task<SubstitutionDto> VoteOnSubstitutionAsync(
            Guid substitutionId,
            Guid userId,
            bool isPositive,
            string? comment = null,
            CancellationToken ct = default)
        {
            var substitution = await _db.IngredientSubstitutions
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Include(s => s.SuggestedByUser)
                .FirstOrDefaultAsync(s => s.Id == substitutionId, ct);

            if (substitution == null)
            {
                throw new InvalidOperationException("Substitution not found");
            }

            // Check for existing vote
            var existingVote = await _db.SubstitutionVotes
                .FirstOrDefaultAsync(v => v.SubstitutionId == substitutionId && v.UserId == userId, ct);

            if (existingVote != null)
            {
                // Update existing vote
                if (existingVote.IsPositive != isPositive)
                {
                    // Switching vote direction
                    if (isPositive)
                    {
                        substitution.PositiveVotes++;
                        substitution.NegativeVotes--;
                    }
                    else
                    {
                        substitution.PositiveVotes--;
                        substitution.NegativeVotes++;
                    }
                    existingVote.IsPositive = isPositive;
                    existingVote.Comment = comment;
                }
            }
            else
            {
                // New vote
                var vote = new SubstitutionVote
                {
                    UserId = userId,
                    SubstitutionId = substitutionId,
                    IsPositive = isPositive,
                    Comment = comment,
                    CreatedAt = DateTime.UtcNow
                };
                _db.SubstitutionVotes.Add(vote);

                if (isPositive)
                    substitution.PositiveVotes++;
                else
                    substitution.NegativeVotes++;
            }

            // Recalculate confidence score (0-100)
            var totalVotes = substitution.PositiveVotes + substitution.NegativeVotes;
            if (totalVotes > 0)
            {
                substitution.ConfidenceScore = (int)((substitution.PositiveVotes / (double)totalVotes) * 100);
            }

            // Auto-verify if high confidence with enough votes
            if (substitution.ConfidenceScore >= 80 && totalVotes >= 10)
            {
                substitution.IsVerified = true;
            }

            await _db.SaveChangesAsync(ct);

            return MapToDto(substitution);
        }

        public async Task<List<BulkSubstitutionResponseDto>> GetBulkSubstitutionsAsync(
            BulkSubstitutionRequestDto request,
            CancellationToken ct = default)
        {
            var results = new List<BulkSubstitutionResponseDto>();

            foreach (var ingredientName in request.IngredientNames)
            {
                var normalizedName = ingredientName.ToLowerInvariant().Trim();

                // Find the global ingredient
                var ingredient = await _db.GlobalIngredients
                    .Include(g => g.Allergens)
                    .Where(g => g.Name.ToLower() == normalizedName ||
                               g.NormalizedName.ToLower() == normalizedName)
                    .FirstOrDefaultAsync(ct);

                var response = new BulkSubstitutionResponseDto
                {
                    OriginalIngredient = ingredientName
                };

                if (ingredient != null)
                {
                    // Check if ingredient triggers any of user's allergies
                    if (request.UserAllergyIds != null && request.UserAllergyIds.Any())
                    {
                        var triggeredAllergies = await _db.Allergies
                            .Where(a => request.UserAllergyIds.Contains(a.Id))
                            .Where(a => ingredient.Allergens.Any(ia =>
                                ia.AllergenType.ToLower() == a.Name.ToLower()))
                            .Select(a => a.Name)
                            .ToListAsync(ct);

                        response.HasAllergen = triggeredAllergies.Any();
                        response.TriggeredAllergies = triggeredAllergies;
                    }

                    // Get substitutions
                    var substitutions = await GetSubstitutionsForIngredientAsync(ingredient.Id, ct);
                    response.Substitutions = substitutions;
                }

                results.Add(response);
            }

            return results;
        }

        public async Task IncrementUsageCountAsync(Guid substitutionId, CancellationToken ct = default)
        {
            await _db.IngredientSubstitutions
                .Where(s => s.Id == substitutionId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsageCount, x => x.UsageCount + 1), ct);
        }

        public async Task<List<SubstitutionDto>> GetTopSubstitutionsAsync(
            int take = 20,
            CancellationToken ct = default)
        {
            var substitutions = await _db.IngredientSubstitutions
                .Include(s => s.OriginalIngredient)
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.Allergy)
                .Include(s => s.SuggestedByUser)
                .Where(s => s.IsVerified || s.ConfidenceScore >= 70)
                .OrderByDescending(s => s.UsageCount)
                .ThenByDescending(s => s.ConfidenceScore)
                .Take(take)
                .AsNoTracking()
                .ToListAsync(ct);

            return substitutions.Select(MapToDto).ToList();
        }

        private async Task<GlobalIngredient> ResolveOrCreateIngredientAsync(string name, CancellationToken ct)
        {
            var normalized = name.ToLowerInvariant().Trim();

            var ingredient = await _db.GlobalIngredients
                .FirstOrDefaultAsync(g => g.NormalizedName == normalized ||
                                          (g.Aliases != null && g.Aliases.ToLower().Contains(normalized)), ct);

            if (ingredient == null)
            {
                ingredient = new GlobalIngredient
                {
                    Id = Guid.NewGuid(),
                    Name = name.Trim(),
                    NormalizedName = normalized,
                    IsVerified = false,
                    CreatedAt = DateTime.UtcNow
                };
                _db.GlobalIngredients.Add(ingredient);
                await _db.SaveChangesAsync(ct);
            }

            return ingredient;
        }

        private static SubstitutionDto MapToDto(IngredientSubstitution s)
        {
            return new SubstitutionDto
            {
                Id = s.Id,
                OriginalIngredientId = s.OriginalIngredientId,
                OriginalIngredientName = s.OriginalIngredient?.Name ?? "",
                SubstituteIngredientId = s.SubstituteIngredientId,
                SubstituteIngredientName = s.SubstituteIngredient?.Name ?? "",
                Reason = s.Reason.ToString(),
                AllergyId = s.AllergyId,
                AllergyName = s.Allergy?.Name,
                ConversionRatio = s.ConversionRatio,
                Notes = s.Notes,
                ConfidenceScore = s.ConfidenceScore,
                PositiveVotes = s.PositiveVotes,
                NegativeVotes = s.NegativeVotes,
                UsageCount = s.UsageCount,
                IsVerified = s.IsVerified,
                SuggestedByUserId = s.SuggestedByUserId,
                SuggestedByUsername = s.SuggestedByUser?.Username,
                CreatedAt = s.CreatedAt
            };
        }
    }
}
