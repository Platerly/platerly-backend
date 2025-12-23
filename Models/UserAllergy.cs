using Microsoft.EntityFrameworkCore;

namespace Platerly.Api.Models
{
    public class UserAllergy
    {
        public Guid UserId { get; set; }
        public User User { get; set; } = null;

        public Guid AllergyId { get; set; }
        public Allergy Allergy { get; set; } = null;

        public bool IsEnabled { get; set; } = true;

        //minimum severity to warn about
        public AllergenSeverity MinSeverity { get; set; } = AllergenSeverity.Low;

        //Whether to show "trace/may contain" hits
        public bool IncludeTrace { get; set; } = true;
    }
}
