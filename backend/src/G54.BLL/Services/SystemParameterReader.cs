using G54.DAL.Uow;

namespace G54.BLL.Services;

public sealed class SystemParameterReader(IUnitOfWork uow) : ISystemParameterReader
{
    private readonly Dictionary<string, long> _cache = new(StringComparer.Ordinal);

    public async Task<long> GetLongAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var param = await uow.SystemParameters.GetByKeyAsync(key, ct)
            ?? throw new InvalidOperationException($"System parameter '{key}' not found or has no numeric value.");

        if (param.NumericValue is null)
        {
            throw new InvalidOperationException($"System parameter '{key}' does not have a numeric value.");
        }

        var value = param.NumericValue.Value;
        _cache[key] = value;
        return value;
    }
}
