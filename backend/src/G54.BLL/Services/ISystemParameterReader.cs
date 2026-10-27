namespace G54.BLL.Services;

public interface ISystemParameterReader
{
    /// <summary>
    /// Numeric value of a parameter; throws InvalidOperationException if missing/non-numeric (seeded by migration).
    /// </summary>
    Task<long> GetLongAsync(string key, CancellationToken ct = default);
}
