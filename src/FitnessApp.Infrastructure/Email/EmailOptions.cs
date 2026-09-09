namespace FitnessApp.Infrastructure.Email;

public sealed class EmailDeliveryOptions
{
    public const string SectionName = "Email";

    public string Transport { get; set; } = "Smtp";

    public int QueueCapacity { get; set; } = 32;

    public int MaxAttempts { get; set; } = 2;

    public int RetryDelaySeconds { get; set; } = 2;

    public string PickupDirectory { get; set; } = string.Empty;
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 15;
}
