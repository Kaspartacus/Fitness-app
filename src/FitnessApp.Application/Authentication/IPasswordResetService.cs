namespace FitnessApp.Application.Authentication;

public interface IPasswordResetService
{
    Task RequestAsync(string email, CancellationToken cancellationToken);

    Task<PasswordResetResult> ResetAsync(
        string email,
        string encodedToken,
        string newPassword,
        CancellationToken cancellationToken);
}
