using System.Security.Cryptography;

namespace G4.Infrastructure.Authentication;

// Format read by AuthenticationService.Verify: PBKDF2$iterations$salt$hash.
public static class PasswordHashing
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(24);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            210000,
            HashAlgorithmName.SHA256,
            32
        );
        return $"PBKDF2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
