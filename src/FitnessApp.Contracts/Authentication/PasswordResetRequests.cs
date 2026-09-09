using System.ComponentModel.DataAnnotations;

namespace FitnessApp.Contracts.Authentication;

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Indtast din e-mailadresse.")]
    [EmailAddress(ErrorMessage = "Indtast en gyldig e-mailadresse.")]
    [StringLength(254, ErrorMessage = "E-mailadressen må højst være 254 tegn.")]
    public string Email { get; set; } = string.Empty;
}

public sealed record ForgotPasswordResponse(string Message);

public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = "Resetlinket mangler en e-mailadresse.")]
    [EmailAddress(ErrorMessage = "Resetlinket indeholder en ugyldig e-mailadresse.")]
    [StringLength(254, ErrorMessage = "E-mailadressen må højst være 254 tegn.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Resetlinket mangler en sikkerhedskode.")]
    [StringLength(4096, ErrorMessage = "Resetlinkets sikkerhedskode er ugyldig.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indtast en ny adgangskode.")]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Adgangskoden skal være mellem 12 og 128 tegn.")]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$",
        ErrorMessage = "Adgangskoden skal indeholde små og store bogstaver, et tal og et specialtegn.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Gentag den nye adgangskode.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Adgangskoderne er ikke ens.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
