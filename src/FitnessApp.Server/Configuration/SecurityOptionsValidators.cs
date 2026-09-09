using System.Net.Mail;
using FitnessApp.Application.Authentication;
using FitnessApp.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace FitnessApp.Server.Configuration;

internal sealed class PasswordResetOptionsValidator : IValidateOptions<PasswordResetOptions>
{
    public ValidateOptionsResult Validate(string? name, PasswordResetOptions options)
    {
        if (options.TokenLifetimeMinutes is < 5 or > 1440)
        {
            return ValidateOptionsResult.Fail(
                "Authentication:PasswordReset:TokenLifetimeMinutes must be between 5 and 1440.");
        }

        if (options.CooldownSeconds is < 0 or > 86400)
        {
            return ValidateOptionsResult.Fail(
                "Authentication:PasswordReset:CooldownSeconds must be between 0 and 86400.");
        }

        return options.MinimumResponseMilliseconds is < 0 or > 5000
            ? ValidateOptionsResult.Fail(
                "Authentication:PasswordReset:MinimumResponseMilliseconds must be between 0 and 5000.")
            : ValidateOptionsResult.Success;
    }
}

internal sealed class PublicAppOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<PublicAppOptions>
{
    public ValidateOptionsResult Validate(string? name, PublicAppOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath is not "/")
        {
            return ValidateOptionsResult.Fail(
                "PublicApp:BaseUrl must be an absolute origin without credentials, a path, query, or fragment.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return ValidateOptionsResult.Success;
        }

        var localDevelopmentHttp = environment.IsDevelopment() &&
            uri.Scheme == Uri.UriSchemeHttp &&
            (uri.Host is "localhost" or "127.0.0.1" or "::1");
        return localDevelopmentHttp
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "PublicApp:BaseUrl must use HTTPS except for an explicit loopback Development URL.");
    }
}

internal sealed class EmailDeliveryOptionsValidator(IWebHostEnvironment environment)
    : IValidateOptions<EmailDeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailDeliveryOptions options)
    {
        if (options.Transport is not ("Smtp" or "Pickup"))
        {
            return ValidateOptionsResult.Fail("Email:Transport must be Smtp or Pickup.");
        }

        if (options.Transport is "Pickup" &&
            !environment.IsDevelopment() &&
            !environment.IsEnvironment("Testing"))
        {
            return ValidateOptionsResult.Fail(
                "The Pickup email transport is restricted to Development and Testing.");
        }

        if (options.Transport is "Pickup" && string.IsNullOrWhiteSpace(options.PickupDirectory))
        {
            return ValidateOptionsResult.Fail("Email:PickupDirectory is required for the Pickup transport.");
        }

        if (options.Transport is "Pickup")
        {
            var pickupPath = Path.GetFullPath(options.PickupDirectory);
            var webRootPath = Path.GetFullPath(
                environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
            var webRootPrefix = webRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if ((pickupPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .StartsWith(webRootPrefix, pathComparison))
            {
                return ValidateOptionsResult.Fail(
                    "Email:PickupDirectory must be outside the public web root.");
            }
        }

        if (options.QueueCapacity is < 1 or > 1000 ||
            options.MaxAttempts is < 1 or > 5 ||
            options.RetryDelaySeconds is < 0 or > 60)
        {
            return ValidateOptionsResult.Fail("Email queue and retry settings are outside the permitted range.");
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class SmtpOptionsValidator(IOptions<EmailDeliveryOptions> emailOptions)
    : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (emailOptions.Value.Transport is not "Smtp")
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.Host) ||
            string.IsNullOrWhiteSpace(options.Username) ||
            string.IsNullOrWhiteSpace(options.Password) ||
            string.IsNullOrWhiteSpace(options.FromName) ||
            !MailAddress.TryCreate(options.FromEmail, out _) ||
            options.Port is not 587 ||
            options.TimeoutSeconds is < 1 or > 60)
        {
            return ValidateOptionsResult.Fail(
                "SMTP configuration is incomplete or invalid. Port 587 with required STARTTLS is supported.");
        }

        return ValidateOptionsResult.Success;
    }
}

internal static class PrivateDirectory
{
    public static string ResolveAndCreate(
        string? configuredPath,
        string contentRootPath,
        string webRootPath,
        string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException($"{configurationKey} is required.");
        }

        var path = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath));
        var webRoot = Path.GetFullPath(webRootPath).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var pathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if ((path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .StartsWith(webRoot, pathComparison))
        {
            throw new InvalidOperationException($"{configurationKey} must be outside the public web root.");
        }

        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }
}
