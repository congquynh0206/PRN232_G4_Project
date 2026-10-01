using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Authentication;

public sealed class AuthenticationService(ApplicationDbContext db) : IAuthenticationService
{
    public async Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return null;
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email != null && x.Email.ToLower() == normalized, ct);
        return user is not null && Verify(password, user.Password) ? user : null;
    }

    private static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
