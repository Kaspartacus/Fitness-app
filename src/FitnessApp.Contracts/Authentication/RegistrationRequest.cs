using System.ComponentModel.DataAnnotations;

namespace FitnessApp.Contracts.Authentication;

public sealed class RegistrationRequest
{
    [Required(ErrorMessage = "Indtast dit navn.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Navnet skal være mellem 2 og 100 tegn.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indtast din e-mailadresse.")]
    [EmailAddress(ErrorMessage = "Indtast en gyldig e-mailadresse.")]
    [StringLength(254, ErrorMessage = "E-mailadressen må højst være 254 tegn.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indtast en adgangskode.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Adgangskoden skal være mellem 12 og 128 tegn.")]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$",
        ErrorMessage = "Adgangskoden skal indeholde små og store bogstaver, et tal og et specialtegn.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Gentag adgangskoden.")]
    [Compare(nameof(Password), ErrorMessage = "Adgangskoderne er ikke ens.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
