using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NpgsqlTypes;

namespace G54.DAL;

public partial class AppDbContext
{
    /// <summary>Maps an enum to its <c>[PgName]</c> text, so the stored values stay the UPPER_SNAKE strings the check constraints use.</summary>
    private static ValueConverter<TEnum, string> PgNameConverter<TEnum>()
        where TEnum : struct, Enum
    {
        var names = Enum.GetValues<TEnum>().ToDictionary(
            value => value,
            value => typeof(TEnum).GetField(value.ToString())!.GetCustomAttribute<PgNameAttribute>()!.PgName);
        var values = names.ToDictionary(pair => pair.Value, pair => pair.Key);
        return new ValueConverter<TEnum, string>(value => names[value], text => values[text]);
    }
}
