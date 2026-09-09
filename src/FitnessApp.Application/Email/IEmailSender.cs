namespace FitnessApp.Application.Email;

public sealed record EmailMessage(
    string MessageId,
    string Recipient,
    string Subject,
    string PlainTextBody,
    string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface IEmailQueue
{
    bool TryQueue(EmailMessage message);
}
