namespace Platerly.Api.Models
{
    public class AllergenKeywordAllergy
    {
        public int AllergenKeywordId { get; set; }
        public AllergenKeyword AllergenKeyword { get; set; } = null!;

        //FK must match Allergy.Id (guid)
        public Guid AllergyId { get; set; }
        public Allergy Allergy { get; set; } = null!;
    }
}
