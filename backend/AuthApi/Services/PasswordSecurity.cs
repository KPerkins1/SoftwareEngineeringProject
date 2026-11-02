using System.Security.Cryptography;
using AuthApi.Models;

namespace AuthApi.Services;

// This class handles all password storage and verification.
// We never save a plain-text password; instead, we salt and hash it before storing it.
public static class PasswordSecurity
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 600_000;

    public static UserAccount CreateAccount(string email, string role, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return new UserAccount(
            Guid.NewGuid().ToString("N"),
            email.Trim(),
            role,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public static bool VerifyPassword(UserAccount account, string password)
    {
        try
        {
            var salt = Convert.FromBase64String(account.PasswordSalt);
            var expected = Convert.FromBase64String(account.PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
