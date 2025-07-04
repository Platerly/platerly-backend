namespace Platerly.Api.DTOs
{
    public class CreateCookbookDto
    {
        public string Title { get; set; } = string.Empty;
        public bool IsPublic { get; set; } = false;
    }
}
