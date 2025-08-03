namespace Platerly.Api.DTOs
{
    public class ResetPasswordDto
    {
        public Guid Token { get; set; }
        public string NewPassword { get; set; }
    }

}
