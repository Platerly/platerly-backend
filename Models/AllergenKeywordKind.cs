namespace Platerly.Api.Models
{
    public enum AllergenKeywordKind
    {
        Synonym = 0,     // e.g., "cocoa", "whey", "malt"
        ENumber = 1,     // e.g., "E220", "E223"
        Food = 2,        // concrete foods/ingredients ("barley", "chocolate", "lentils")
        Category = 3,    // broader bucket ("tree nuts", "shellfish", "legumes")
        Family = 4, // taxonomic/protein family if you want it later
        Term = 4
    }
}
