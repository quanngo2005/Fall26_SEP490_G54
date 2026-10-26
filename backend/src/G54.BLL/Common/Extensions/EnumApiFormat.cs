using System.Collections.Concurrent;
using System.Reflection;
using G54.BLL.Common.Exceptions;
using G54.DAL.Entities;
using NpgsqlTypes;

namespace G54.BLL.Common.Extensions;

/// <summary>
/// Converts DAL enums to the UPPER_SNAKE strings exposed by the API, matching their PostgreSQL [PgName].
/// </summary>
public static class EnumApiFormat
{
    public const string RevisionRequiredDecision = "REVISION_REQUIRED";

    private static readonly ConcurrentDictionary<Enum, string> Cache = new();

    public static string ToEnumString<TEnum>(this TEnum value)
        where TEnum : struct, Enum
    {
        return Cache.GetOrAdd(value, static key =>
        {
            var name = key.ToString();
            var field = key.GetType().GetField(name, BindingFlags.Public | BindingFlags.Static);
            return field?.GetCustomAttribute<PgNameAttribute>()?.PgName ?? name.ToUpperInvariant();
        });
    }

    /// <summary>
    /// Parses an UPPER_SNAKE API value (case-insensitive) back to its enum. Returns null when the value is
    /// blank; an unknown value is a 400 VALIDATION_ERROR. Inverse of <see cref="ToEnumString{TEnum}"/>.
    /// </summary>
    public static TEnum? ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToEnumString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new ValidationException($"Giá trị '{value}' không hợp lệ cho tham số '{field}'.");
    }

    /// <summary>Like <see cref="ParseEnum{TEnum}"/> but a blank value is also a 400 VALIDATION_ERROR.</summary>
    public static TEnum ParseRequiredEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        return ParseEnum<TEnum>(value, field)
            ?? throw new ValidationException($"Tham số '{field}' là bắt buộc.");
    }

    /// <summary>
    /// API vocabulary for verification decisions: NEED_MORE_INFO is exposed as REVISION_REQUIRED.
    /// </summary>
    public static string ToApiDecision(this VerificationDecision decision)
    {
        return decision == VerificationDecision.NeedMoreInfo ? RevisionRequiredDecision : decision.ToEnumString();
    }
}
