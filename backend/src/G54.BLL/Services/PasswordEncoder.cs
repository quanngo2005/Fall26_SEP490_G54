using System.Security.Cryptography;

namespace G54.BLL.Services;

public sealed class PasswordEncoder : IPasswordEncoder
{
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Format = "pbkdf2-sha256";

    public string Encode(string rawPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawPassword);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(rawPassword, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Format, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string rawPassword, string encodedPassword)
    {
        if (string.IsNullOrEmpty(rawPassword) || string.IsNullOrEmpty(encodedPassword))
        {
            return false;
        }

        var parts = encodedPassword.Split('$');
        if (parts.Length != 4
            || parts[0] != Format
            || !int.TryParse(parts[1], out var iterations)
            || iterations < 100_000
            || iterations > 2_000_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltSize || expectedHash.Length != HashSize)
            {
                return false;
            }

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(rawPassword, salt, iterations, HashAlgorithmName.SHA256, HashSize);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
