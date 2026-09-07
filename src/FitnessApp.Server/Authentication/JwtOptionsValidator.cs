using FitnessApp.Application.Authentication;
using Microsoft.Extensions.Options;

namespace FitnessApp.Server.Authentication;

internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"{JwtOptions.SectionName}:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{JwtOptions.SectionName}:Audience is required.");
        }

        if (options.AccessTokenMinutes is < 1 or > 60)
        {
            failures.Add($"{JwtOptions.SectionName}:AccessTokenMinutes must be between 1 and 60.");
        }

        if (options.ClockSkewSeconds is < 0 or > 120)
        {
            failures.Add($"{JwtOptions.SectionName}:ClockSkewSeconds must be between 0 and 120.");
        }

        try
        {
            if (Convert.FromBase64String(options.SigningKey).Length < 32)
            {
                failures.Add($"{JwtOptions.SectionName}:SigningKey must contain at least 32 random bytes encoded as Base64.");
            }
        }
        catch (FormatException)
        {
            failures.Add($"{JwtOptions.SectionName}:SigningKey must be valid Base64.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
