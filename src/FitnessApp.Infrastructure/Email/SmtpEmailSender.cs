using FitnessApp.Application.Email;
using FitnessApp.Application.Authentication;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using MimeKit;

namespace FitnessApp.Infrastructure.Email;

internal sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    IHostEnvironment environment,
    IOptions<PublicAppOptions> publicAppOptions,
    IServer server,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.AllowLocalDevelopmentRevocationBypass)
        {
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            if (!environment.IsDevelopment() ||
                !Uri.TryCreate(publicAppOptions.Value.BaseUrl, UriKind.Absolute, out var origin) ||
                !origin.IsLoopback ||
                addresses is null || addresses.Count == 0 ||
                addresses.Any(address => !Uri.TryCreate(address, UriKind.Absolute, out var uri) || !uri.IsLoopback))
            {
                throw new InvalidOperationException(
                    "SMTP revocation bypass requires Development, a loopback public origin, and exclusively loopback server listeners.");
            }

            logger.LogWarning(new EventId(1312, "LocalSmtpRevocationBypass"),
                "Local Development SMTP certificate revocation checking is disabled by explicit configuration. Other TLS certificate checks remain enabled.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        mail.To.Add(MailboxAddress.Parse(message.Recipient));
        mail.Subject = message.Subject;
        mail.Body = new BodyBuilder
        {
            TextBody = message.PlainTextBody,
            HtmlBody = message.HtmlBody
        }.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = checked(settings.TimeoutSeconds * 1000),
            CheckCertificateRevocation = !settings.AllowLocalDevelopmentRevocationBypass
        };
        await client.ConnectAsync(
            settings.Host,
            settings.Port,
            SecureSocketOptions.StartTls,
            timeout.Token);
        await client.AuthenticateAsync(settings.Username, settings.Password, timeout.Token);
        await client.SendAsync(mail, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }
}
