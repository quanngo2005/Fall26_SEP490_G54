using System.Globalization;
using G54.BLL.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Process-wide snapshot of the effective system parameters: canonical defaults overlaid with values saved in PostgreSQL
/// and synchronized via Redis (<see cref="SystemParameterService.RedisCacheKey"/>). Consumers keep injecting
/// <c>IOptions&lt;SystemParametersOptions&gt;</c>; <see cref="LiveSystemParametersOptions"/> returns the latest snapshot.
/// </summary>
public sealed class SystemParameterStore
{
    private volatile SystemParametersOptions _current;

    public SystemParameterStore(IConfiguration? configuration = null)
    {
        var defaults = new SystemParametersOptions();
        configuration?.GetSection(SystemParametersOptions.SectionName).Bind(defaults);
        Defaults = defaults;
        _current = defaults.Clone();
    }

    public SystemParametersOptions Current => _current;

    public SystemParametersOptions Defaults { get; }

    public void ApplyTextOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        var next = Defaults.Clone();
        foreach (var (key, text) in overrides)
        {
            var definition = SystemParameterCatalog.Find(key);
            if (definition is { Locked: false } && definition.TryValidateStoredText(text, out var normalized))
            {
                definition.ApplyText?.Invoke(next, normalized);
            }
        }

        _current = next;
    }

    public void Apply(IReadOnlyDictionary<string, decimal> overrides)
    {
        var textOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
        {
            textOverrides[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        ApplyTextOverrides(textOverrides);
    }
}

public sealed class LiveSystemParametersOptions(SystemParameterStore store) : IOptions<SystemParametersOptions>
{
    public SystemParametersOptions Value => store.Current;
}
