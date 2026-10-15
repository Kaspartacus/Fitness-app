using System.Security.Cryptography;
using System.Text;
using FitnessApp.Application.Authentication;
using Microsoft.Extensions.Options;

namespace FitnessApp.Server.Authentication;

// Only Server owns the signing key. Domain separation prevents this derivation from
// being confused with JWT signatures or any other use of that existing stable key.
internal sealed class RefreshCredentialDeriver(IOptions<JwtOptions> options) : IRefreshCredentialDeriver
{
    public string DeriveSuccessor(string credential) => Convert.ToHexString(HMACSHA256.HashData(
        Convert.FromBase64String(options.Value.SigningKey),
        Encoding.UTF8.GetBytes("FitnessApp.Refresh.Successor.v1\0" + credential)));
}
