using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Extensions;
using Platerly.Api.Models;
using Platerly.Api.Services.Notifications;

namespace Platerly.Api.ApiControllers
{
    /// <summary>
    /// B2C controller for managing the authenticated user's social follow graph.
    /// Complements the follow shortcuts on <c>UserController</c> with richer behaviour:
    /// follow actions trigger in-app notifications via <see cref="INotificationService"/>.
    /// Base route: <c>api/follow</c>.
    /// All endpoints require a valid JWT (<c>[Authorize]</c> on the class).
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FollowController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;
        private readonly INotificationService _notificationService;

        public FollowController(PlaterlyDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Follow the specified user. Prevents self-follow and duplicate follows.
        /// Fires a follow notification to the target user on success.
        /// </summary>
        /// <param name="targetUserId">The GUID of the user to follow.</param>
        /// <returns>
        /// <c>200 OK</c> — "Followed successfully."<br/>
        /// <c>400</c> — attempting to follow self, or already following.<br/>
        /// <c>401</c> — invalid/missing JWT claim.
        /// </returns>
        [HttpPost("{targetUserId}")]
        public async Task<IActionResult> Follow(Guid targetUserId)
        {
            if (!User.TryGetUserId(out var currentUserId))
                return Unauthorized("Invalid user token.");

            if (currentUserId == targetUserId)
                return BadRequest("You cannot follow yourself.");

            var existing = await _context.UserFollows.FindAsync(currentUserId, targetUserId);
            if (existing != null)
                return BadRequest("You are already following this user.");

            var follow = new UserFollow
            {
                FollowerId = currentUserId,
                FollowingId = targetUserId
            };

            _context.UserFollows.Add(follow);
            await _context.SaveChangesAsync();

            // Notify the user being followed
            await _notificationService.NotifyFollowAsync(targetUserId, currentUserId);

            return Ok("Followed successfully.");
        }

        /// <summary>
        /// Unfollow a user the authenticated user is currently following.
        /// </summary>
        /// <param name="targetUserId">The GUID of the user to unfollow.</param>
        /// <returns>
        /// <c>200 OK</c> — "Unfollowed successfully."<br/>
        /// <c>401</c> — invalid/missing JWT claim.<br/>
        /// <c>404</c> — follow relationship does not exist.
        /// </returns>
        [HttpDelete("{targetUserId}")]
        public async Task<IActionResult> Unfollow(Guid targetUserId)
        {
            if (!User.TryGetUserId(out var currentUserId))
                return Unauthorized("Invalid user token.");

            var existing = await _context.UserFollows.FindAsync(currentUserId, targetUserId);
            if (existing == null)
                return NotFound("You are not following this user.");

            _context.UserFollows.Remove(existing);
            await _context.SaveChangesAsync();

            return Ok("Unfollowed successfully.");
        }

        /// <summary>
        /// List all users who follow the authenticated user.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="FollowUserDto"/> (id, username, displayName, profileImageUrl).<br/>
        /// <c>401</c> — invalid/missing JWT claim.
        /// </returns>
        [HttpGet("followers")]
        public async Task<ActionResult<IEnumerable<FollowUserDto>>> GetFollowers()
        {
            if (!User.TryGetUserId(out var currentUserId))
                return Unauthorized("Invalid user token.");

            var followers = await _context.UserFollows
                .Where(f => f.FollowingId == currentUserId)
                .Include(f => f.Follower)
                .Select(f => new FollowUserDto
                {
                    Id = f.Follower!.Id,
                    Username = f.Follower.Username,
                    DisplayName = f.Follower.DisplayName,
                    ProfileImageUrl = f.Follower.ProfileImageUrl
                })
                .ToListAsync();

            return Ok(followers);
        }

        /// <summary>
        /// List all users the authenticated user is currently following.
        /// </summary>
        /// <returns>
        /// <c>200 OK</c> — array of <see cref="FollowUserDto"/>.<br/>
        /// <c>401</c> — invalid/missing JWT claim.
        /// </returns>
        [HttpGet("following")]
        public async Task<ActionResult<IEnumerable<FollowUserDto>>> GetFollowing()
        {
            if (!User.TryGetUserId(out var currentUserId))
                return Unauthorized("Invalid user token.");

            var following = await _context.UserFollows
                .Where(f => f.FollowerId == currentUserId)
                .Include(f => f.Following)
                .Select(f => new FollowUserDto
                {
                    Id = f.Following!.Id,
                    Username = f.Following.Username,
                    DisplayName = f.Following.DisplayName,
                    ProfileImageUrl = f.Following.ProfileImageUrl
                })
                .ToListAsync();

            return Ok(following);
        }
    }
}
