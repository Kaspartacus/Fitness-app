using FitnessApp.Application.Email;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FitnessApp.Infrastructure.Email;

internal sealed class PickupDirectoryEmailSender(IOptions<EmailDeliveryOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var directory = options.Value.PickupDirectory;
        Directory.CreateDirectory(directory);
        TryRestrictDirectory(directory);

        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress("FitnessApp testtransport", "no-reply@localhost"));
        mail.To.Add(MailboxAddress.Parse(message.Recipient));
        mail.Subject = message.Subject;
        mail.Body = new BodyBuilder
        {
            TextBody = message.PlainTextBody,
            HtmlBody = message.HtmlBody
        }.ToMessageBody();

        var path = Path.Combine(directory, $"{message.MessageId}.eml");
        await using (var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous))
        {
            await mail.WriteToAsync(stream, cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void TryRestrictDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
