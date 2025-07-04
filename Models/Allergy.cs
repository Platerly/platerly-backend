using System;
using System.Collections.Generic;

namespace Platerly.Api.Models
{
    public class Allergy
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        public ICollection<UserAllergy> UserAllergies { get; set; } = new List<UserAllergy>();
    }
}
