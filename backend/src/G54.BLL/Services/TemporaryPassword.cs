using System.Security.Cryptography;

namespace G54.BLL.Services;

/// <summary>Generates the one-time password handed to a newly created account (employee import and Admin create).</summary>
internal static class TemporaryPassword
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%";
    private const int Length = 16;

    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
