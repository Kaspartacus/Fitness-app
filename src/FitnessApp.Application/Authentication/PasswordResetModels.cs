namespace FitnessApp.Application.Authentication;

public static class PasswordResetConstants
{
    public const string TokenProviderName = "PasswordReset";
}

public sealed class PasswordResetOptions
{
    public const string SectionName = "Authentication:PasswordReset";

    public int TokenLifetimeMinutes { get; set; } = 60;

    public int CooldownSeconds { get; set; } = 300;

    public int MinimumResponseMilliseconds { get; set; } = 200;
}

public sealed class PublicAppOptions
{
    public const string SectionName = "PublicApp";

    public string BaseUrl { get; set; } = string.Empty;
}

public enum PasswordResetStatus
{
    Succeeded,
    InvalidOrExpired,
    InvalidPassword
}

public sealed record PasswordResetResult(
    PasswordResetStatus Status,
    IReadOnlyCollection<string> Errors)
{
    public static PasswordResetResult Succeeded() => new(PasswordResetStatus.Succeeded, []);

    public static PasswordResetResult InvalidOrExpired() =>
        new(PasswordResetStatus.InvalidOrExpired, []);

    public static PasswordResetResult InvalidPassword(IReadOnlyCollection<string> errors) =>
        new(PasswordResetStatus.InvalidPassword, errors);
}
