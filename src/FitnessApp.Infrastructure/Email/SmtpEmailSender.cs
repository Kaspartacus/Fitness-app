using FitnessApp.Application.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FitnessApp.Infrastructure.Email;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
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
            Timeout = checked(settings.TimeoutSeconds * 1000)
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
