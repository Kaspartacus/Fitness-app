using Microsoft.AspNetCore.DataProtection;

namespace FitnessApp.Infrastructure.Diary;

public sealed class DiaryCipher(IDataProtectionProvider provider)
{
    // Bind ciphertext to both owner and diary date, preventing cross-row substitution.
    private IDataProtector Protector(string owner, DateOnly date) => provider.CreateProtector(
        "FitnessApp.Diary.Text.v1", owner, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    public string Encrypt(string owner, DateOnly date, string text) => Protector(owner, date).Protect(text);
    public string Decrypt(string owner, DateOnly date, string ciphertext) => Protector(owner, date).Unprotect(ciphertext);
}
