using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>
    /// B2C user logic layer — enforces the weekly allergen-scan quota for free-tier users.
    /// Depends on <see cref="PlaterlyDbContext"/> to read user tier and read/write
    /// <c>UserAllergenScans</c> rows.
    ///
    /// Quota rule: free users may scan 3 distinct recipes per calendar week (Monday midnight UTC).
    /// Premium users and users on an active 7-day trial bypass the quota entirely — no DB row is written.
    /// A unique index on <c>(UserId, RecipeId, WeekStartDate)</c> prevents race-condition double-counting
    /// when concurrent requests arrive for the same recipe in the same week.
    /// </summary>
    public class AllergenScanService : IAllergenScanService
    {
        /// <summary>Maximum number of unique recipes a free user may scan per week.</summary>
        private const int FREE_SCAN_LIMIT = 3;

        private readonly PlaterlyDbContext _db;

        public AllergenScanService(PlaterlyDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns Monday midnight UTC for the ISO calendar week containing <paramref name="date"/>.
        /// </summary>
        /// <remarks>
        /// Formula: <c>((int)dayOfWeek + 6) % 7</c> converts .NET's Sunday=0 … Saturday=6 into
        /// Monday=0 … Sunday=6, giving the number of days to subtract to reach the previous Monday.
        /// Example: Wednesday (DayOfWeek=3) → (3+6)%7 = 2 days back → Monday.
        /// </remarks>
        /// <param name="date">Any UTC date within the target week.</param>
        /// <returns>Monday 00:00:00 UTC of the week containing <paramref name="date"/>.</returns>
        public DateTime GetWeekStart(DateTime date)
        {
            // DayOfWeek: Sun=0, Mon=1 … Sat=6
            // (dayOfWeek + 6) % 7 gives days since Monday (Mon=0 … Sun=6)
            var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-daysSinceMonday);
        }

        private static bool IsActiveSubscriber(Models.User user)
        {
            if (user.IsPremium) return true;
            if (user.TrialEndUtc.HasValue && user.TrialEndUtc.Value > DateTimeOffset.UtcNow) return true;
            return false;
        }

        /// <summary>
        /// Attempts to consume one scan from the user's weekly quota.
        /// </summary>
        /// <remarks>
        /// Premium / active trial users: returns <c>true</c> immediately with no DB write.
        /// Free users: checks whether the same recipe was already scanned this week (free pass);
        /// if not, counts distinct recipes scanned this week and blocks if at limit.
        /// Inserts a new <c>UserAllergenScan</c> row when under the limit; a <see cref="DbUpdateException"/>
        /// on unique-index violation (race condition) is swallowed and treated as "already scanned".
        /// </remarks>
        /// <param name="userId">The authenticated user's ID.</param>
        /// <param name="recipeId">The recipe being scanned.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> if the scan is permitted; <c>false</c> if the weekly quota is exhausted.</returns>
        public async Task<bool> TryConsumeOrCountScanAsync(Guid userId, Guid recipeId, CancellationToken ct = default)
        {
            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsPremium, u.TrialEndUtc })
                .FirstOrDefaultAsync(ct);

            if (user == null) return false;

            // Premium / active trial → unlimited scans, no DB write
            if (user.IsPremium ||
                (user.TrialEndUtc.HasValue && user.TrialEndUtc.Value > DateTimeOffset.UtcNow))
            {
                return true;
            }

            var weekStart = GetWeekStart(DateTime.UtcNow);

            // Already scanned same recipe this week → free pass, no new row
            var alreadyScanned = await _db.UserAllergenScans
                .AnyAsync(s => s.UserId == userId
                            && s.RecipeId == recipeId
                            && s.WeekStartDate == weekStart, ct);

            if (alreadyScanned) return true;

            // Count distinct recipes scanned this week
            var scansUsed = await _db.UserAllergenScans
                .CountAsync(s => s.UserId == userId && s.WeekStartDate == weekStart, ct);

            if (scansUsed >= FREE_SCAN_LIMIT) return false;

            // Under limit — insert scan row (unique index protects against race conditions)
            try
            {
                _db.UserAllergenScans.Add(new UserAllergenScan
                {
                    UserId = userId,
                    RecipeId = recipeId,
                    WeekStartDate = weekStart,
                    ScannedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Unique index violation means a concurrent request already inserted the row — treat as already scanned
            }

            return true;
        }

        /// <summary>
        /// Returns the current week's scan usage for the given user.
        /// Premium and active-trial users receive a <see cref="WeeklyScanStatus"/> with
        /// <c>IsPremium=true</c> and null quota fields (unlimited). Free users receive
        /// scans used, remaining, and the UTC timestamp at which the week resets (next Monday).
        /// </summary>
        /// <param name="userId">The authenticated user's ID.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A <see cref="WeeklyScanStatus"/> describing quota usage for the current week.</returns>
        public async Task<WeeklyScanStatus> GetWeeklyScanStatusAsync(Guid userId, CancellationToken ct = default)
        {
            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.IsPremium, u.TrialEndUtc })
                .FirstOrDefaultAsync(ct);

            var isPremium = user != null &&
                (user.IsPremium ||
                 (user.TrialEndUtc.HasValue && user.TrialEndUtc.Value > DateTimeOffset.UtcNow));

            var weekStart = GetWeekStart(DateTime.UtcNow);
            var weekResetsAt = weekStart.AddDays(7); // next Monday midnight UTC

            if (isPremium)
            {
                return new WeeklyScanStatus(
                    ScansUsed: 0,
                    ScansLimit: null,
                    ScansRemaining: null,
                    WeekResetsAt: weekResetsAt,
                    IsPremium: true
                );
            }

            var scansUsed = await _db.UserAllergenScans
                .CountAsync(s => s.UserId == userId && s.WeekStartDate == weekStart, ct);

            var remaining = Math.Max(0, FREE_SCAN_LIMIT - scansUsed);

            return new WeeklyScanStatus(
                ScansUsed: scansUsed,
                ScansLimit: FREE_SCAN_LIMIT,
                ScansRemaining: remaining,
                WeekResetsAt: weekResetsAt,
                IsPremium: false
            );
        }
    }
}
