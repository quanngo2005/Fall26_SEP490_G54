using G54.BLL.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

/// <summary>
/// Process-wide snapshot of the effective system parameters: appsettings defaults overlaid with the values saved in
/// the database. Consumers keep injecting <c>IOptions&lt;SystemParametersOptions&gt;</c>; <see cref="LiveSystemParametersOptions"/>
/// makes that always return the latest snapshot.
/// </summary>
public sealed class SystemParameterStore
{
    private volatile SystemParametersOptions _current;

    public SystemParameterStore(IConfiguration configuration)
    {
        var defaults = new SystemParametersOptions();
        configuration.GetSection(SystemParametersOptions.SectionName).Bind(defaults);
        Defaults = defaults;
        _current = defaults.Clone();
    }

    public SystemParametersOptions Current => _current;

    public SystemParametersOptions Defaults { get; }

    public void Apply(IReadOnlyDictionary<string, decimal> overrides)
    {
        var next = Defaults.Clone();
        foreach (var (key, value) in overrides)
        {
            SystemParameterCatalog.Find(key)?.Set?.Invoke(next, value);
        }

        _current = next;
    }
}

public sealed class LiveSystemParametersOptions(SystemParameterStore store) : IOptions<SystemParametersOptions>
{
    public SystemParametersOptions Value => store.Current;
}
