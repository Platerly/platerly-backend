using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Allergy
{
    public class AllergenScanService : IAllergenScanService
    {
        private const int FREE_SCAN_LIMIT = 3;

        private readonly PlaterlyDbContext _db;

        public AllergenScanService(PlaterlyDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns Monday midnight UTC for the week containing <paramref name="date"/>.
        /// </summary>
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
