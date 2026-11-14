using System.Security.Cryptography;

namespace HaliYikamaAPI.Services.Auth;

/// <summary>
/// PBKDF2 tabanlı güvenli şifre hashleme ve doğrulama servisi.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16; // 128-bit
    private const int KeySize = 32;  // 256-bit
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    /// Verilen düz metin şifreyi tuzlayarak güvenli şekilde hashler.
    /// </summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithm,
            KeySize);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Girilen şifrenin saklanan hash ile eşleşip eşleşmediğini zamanlama güvenli (timing-safe) kontrol eder.
    /// </summary>
    public static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2)
            return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithm,
            KeySize);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
