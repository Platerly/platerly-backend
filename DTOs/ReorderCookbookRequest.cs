using System;
using System.Collections.Generic;

namespace Platerly.Api.DTOs
{
    public class ReorderCookbookRequest
    {
        public List<Guid> RecipeIds { get; set; } = new();
    }
}
