namespace Platerly.Api.Models
{
    public class UserAllergy
    {
        public Guid UserId { get; set; }
        public User User { get; set; } = null;

        public Guid AllergyId { get; set; }
        public Allergy Allergy { get; set; } = null;
    }
}
