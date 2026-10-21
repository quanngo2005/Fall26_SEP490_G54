namespace G54.BLL.Common.Validation;

/// <summary>Rules for a new password (docs/api/MY_PROFILE_CONTRACT.md, section 2.3).</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    // Shorter local parts ("a@x.com") would reject far too many otherwise good passwords.
    private const int MinEmailLocalPartLength = 3;

    /// <summary>Returns every rule the password breaks; empty when it is acceptable.</summary>
    public static IReadOnlyList<string> Validate(string? newPassword, string? currentPassword, string? email)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(newPassword))
        {
            errors.Add("Mật khẩu mới là bắt buộc.");
            return errors;
        }

        if (newPassword.Length < MinLength || newPassword.Length > MaxLength)
        {
            errors.Add($"Mật khẩu mới phải từ {MinLength} đến {MaxLength} ký tự.");
        }

        if (!newPassword.Any(char.IsLetter) || !newPassword.Any(char.IsDigit))
        {
            errors.Add("Mật khẩu mới phải có ít nhất một chữ cái và một chữ số.");
        }

        if (currentPassword is not null && string.Equals(newPassword, currentPassword, StringComparison.Ordinal))
        {
            errors.Add("Mật khẩu mới phải khác mật khẩu hiện tại.");
        }

        var localPart = email is null ? string.Empty : email.Split('@')[0];
        if (localPart.Length >= MinEmailLocalPartLength
            && newPassword.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Mật khẩu mới không được chứa phần tên trong email của bạn.");
        }

        return errors;
    }
}
