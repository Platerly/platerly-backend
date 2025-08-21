using System;
using System.Collections.Generic;

namespace Platerly.Api.DTOs
{
    public class RemixDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }

        public Guid UserId { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? ProfileImageUrl { get; set; }

        public string Tags { get; set; } = string.Empty;
        public int PrepTimeMinutes { get; set; }
        public int CookTimeMinutes { get; set; }
        public string Difficulty { get; set; } = string.Empty;

        public List<IngredientDto> Ingredients { get; set; } = new();

        public Guid? ParentRecipeId { get; set; }
        public Guid? OriginalRecipeId { get; set; }
    }
}
