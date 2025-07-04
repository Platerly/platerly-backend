using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.DTOs;
using Platerly.Api.Models;
using System.Security.Claims;

namespace Platerly.Api.ApiControllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FollowController : ControllerBase
    {
        private readonly PlaterlyDbContext _context;

        public FollowController(PlaterlyDbContext context)
        {
            _context = context;
        }

        [HttpPost("{targetUserId}")]
        public async Task<IActionResult> Follow(Guid targetUserId)
        {
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

            return Ok("Followed successfully.");
        }

        [HttpDelete("{targetUserId}")]
        public async Task<IActionResult> Unfollow(Guid targetUserId)
        {
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var existing = await _context.UserFollows.FindAsync(currentUserId, targetUserId);
            if (existing == null)
                return NotFound("You are not following this user.");

            _context.UserFollows.Remove(existing);
            await _context.SaveChangesAsync();

            return Ok("Unfollowed successfully.");
        }

        [HttpGet("followers")]
        public async Task<ActionResult<IEnumerable<FollowUserDto>>> GetFollowers()
        {
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

        [HttpGet("following")]
        public async Task<ActionResult<IEnumerable<FollowUserDto>>> GetFollowing()
        {
            var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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
