using System;
using System.Collections.Generic;

namespace Platerly.Api.DTOs
{
    public class CookbookDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public DateTime CreatedAt { get; set; }

        public List<RecipeDto> Recipes { get; set; } = new();
    }
}
