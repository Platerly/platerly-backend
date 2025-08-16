using System;
using System.Collections.Generic;

namespace Platerly.Api.DTOs
{
    public class CreateRemixDto
    {
        public Guid OriginalRecipeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Instructions { get; set; } = string.Empty;
        public string? Region { get; set; }
        public string? ImageUrl { get; set; }
        public string? ImageBlobName { get; set; } // Blob storage name for the image
        public string Tags { get; set; } = string.Empty;
        public int PrepTimeMinutes { get; set; } = 0;
        public int CookTimeMinutes { get; set; } = 0;
        public string Difficulty { get; set; } = string.Empty;

        public List<IngredientDto> Ingredients { get; set; } = new();
    }
}
