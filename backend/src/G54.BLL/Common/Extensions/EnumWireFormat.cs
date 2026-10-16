using System.Collections.Concurrent;
using System.Reflection;
using G54.DAL.Entities;
using NpgsqlTypes;

namespace G54.BLL.Common.Extensions;

/// <summary>
/// Converts DAL enums to the UPPER_SNAKE strings exposed by the API, matching their PostgreSQL [PgName].
/// </summary>
public static class EnumWireFormat
{
    public const string RevisionRequiredDecision = "REVISION_REQUIRED";

    private static readonly ConcurrentDictionary<Enum, string> Cache = new();

    public static string ToWire<TEnum>(this TEnum value)
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
    /// API vocabulary for verification decisions: NEED_MORE_INFO is exposed as REVISION_REQUIRED.
    /// </summary>
    public static string ToApiDecision(this VerificationDecision decision)
    {
        return decision == VerificationDecision.NeedMoreInfo ? RevisionRequiredDecision : decision.ToWire();
    }
}
