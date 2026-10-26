using System.Text.RegularExpressions;

namespace G54.BLL.Common.Validation;

/// <summary>Phone number rule shared by the employee admin API and My Profile so the two cannot drift apart.</summary>
public static partial class PhoneRules
{
    public const int Length = 10;

    [GeneratedRegex(@"^0[0-9]{9}\z")]
    private static partial Regex PhonePattern();

    /// <summary>True for an already trimmed value of exactly 10 digits that starts with 0 (for example <c>0901000004</c>).</summary>
    public static bool IsValid(string trimmedPhone) =>
        trimmedPhone.Length == Length && PhonePattern().IsMatch(trimmedPhone);

    /// <summary>Trims the value; blank input becomes <c>null</c> (meaning "no phone number").</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
