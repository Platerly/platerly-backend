using System;
using System.Threading;
using System.Threading.Tasks;

namespace Platerly.Api.Services.Allergy
{
    public record WeeklyScanStatus(
        int ScansUsed,
        int? ScansLimit,        // null if premium/trial
        int? ScansRemaining,    // null if premium/trial
        DateTime WeekResetsAt,
        bool IsPremium
    );

    public interface IAllergenScanService
    {
        /// <summary>
        /// Returns Monday midnight UTC for the week containing <paramref name="date"/>.
        /// </summary>
        DateTime GetWeekStart(DateTime date);

        /// <summary>
        /// For premium/trial users: always returns true without touching the DB.
        /// For free users: checks/inserts a scan row. Returns false if weekly limit is reached.
        /// </summary>
        Task<bool> TryConsumeOrCountScanAsync(Guid userId, Guid recipeId, CancellationToken ct = default);

        /// <summary>
        /// Returns scan quota status for the current week.
        /// </summary>
        Task<WeeklyScanStatus> GetWeeklyScanStatusAsync(Guid userId, CancellationToken ct = default);
    }
}
