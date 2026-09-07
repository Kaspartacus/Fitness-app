using System.ComponentModel.DataAnnotations;

namespace FitnessApp.Contracts.Authentication;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Indtast din e-mailadresse.")]
    [EmailAddress(ErrorMessage = "Indtast en gyldig e-mailadresse.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indtast din adgangskode.")]
    public string Password { get; set; } = string.Empty;
}
