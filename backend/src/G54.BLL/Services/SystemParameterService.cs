using System.Text.Json;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameter;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.BLL.Services;

public sealed class SystemParameterService(
    AppDbContext db,
    SystemParameterStore store,
    IDistributedCache? cache = null,
    ILogger<SystemParameterService>? logger = null) : ISystemParameterService
{
    public const string RedisCacheKey = "bpms:system-parameters:snapshot";

    private static readonly DistributedCacheEntryOptions CacheEntryOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
    };

    private static readonly Action<ILogger, Exception?> LogRedisSyncWarning =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(10, "SystemParametersRedisSyncFailed"),
            "Could not synchronize system parameters with distributed cache; local snapshot updated.");

    private readonly ILogger<SystemParameterService> _logger = logger ?? NullLogger<SystemParameterService>.Instance;

    public async Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await LoadRowsAsync(ct);
        await PublishAndSyncCacheAsync(rows, ct);
        return await ToDtosAsync(rows, ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (cache is not null)
        {
            try
            {
                var cachedJson = await cache.GetStringAsync(RedisCacheKey, ct);
                if (!string.IsNullOrWhiteSpace(cachedJson))
                {
                    var cachedMap = JsonSerializer.Deserialize<Dictionary<string, string>>(cachedJson);
                    if (cachedMap is not null)
                    {
                        store.ApplyTextOverrides(cachedMap);
                        return;
                    }
                }
            }
#pragma warning disable CA1031 // Distributed cache hiccup must never break DB fallback.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogRedisSyncWarning(_logger, ex);
            }
        }

        var rows = await LoadRowsAsync(ct);
        await PublishAndSyncCacheAsync(rows, ct);
    }

    public async Task<IReadOnlyList<SystemParameterDto>> UpdateAsync(UpdateSystemParametersDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Changes is not { Count: > 0 })
        {
            throw new ValidationException("Cần ít nhất một tham số để cập nhật.");
        }

        var errors = new List<string>();
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var change in request.Changes)
        {
            var definition = SystemParameterCatalog.Find(change?.Key);
            if (change is null || definition is null)
            {
                errors.Add($"Tham số '{change?.Key}' không tồn tại.");
            }
            else if (definition.Locked)
            {
                errors.Add($"Tham số '{definition.Key}' được cố định theo quy định, không thể thay đổi.");
            }
            else if (parsed.ContainsKey(definition.Key))
            {
                errors.Add($"Tham số '{definition.Key}' bị trùng trong yêu cầu.");
            }
            else if (!definition.TryValidateAndFormat(change.RawValue, out var formattedText, out var validationError))
            {
                errors.Add(validationError!);
            }
            else
            {
                parsed.Add(definition.Key, formattedText);
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var keys = parsed.Keys.ToList();
        var existing = await db.SystemParameters.Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, ct);
        foreach (var (key, text) in parsed)
        {
            if (existing.TryGetValue(key, out var row))
            {
                row.Value = text;
            }
            else
            {
                db.SystemParameters.Add(new SystemParameter { Key = key, Value = text });
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw new ConflictException("Tham số vừa được người khác cập nhật. Vui lòng tải lại và thử lại.");
        }

        var rows = await LoadRowsAsync(ct);
        await PublishAndSyncCacheAsync(rows, ct);
        return await ToDtosAsync(rows, ct);
    }

    private async Task<Dictionary<string, SystemParameter>> LoadRowsAsync(CancellationToken ct)
        => await db.SystemParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, ct);

    private async Task PublishAndSyncCacheAsync(Dictionary<string, SystemParameter> rows, CancellationToken ct)
    {
        var validOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, row) in rows)
        {
            var definition = SystemParameterCatalog.Find(key);
            if (definition is { Locked: false } && definition.TryValidateStoredText(row.Value, out var normalized))
            {
                validOverrides[key] = normalized;
            }
        }

        store.ApplyTextOverrides(validOverrides);

        if (cache is not null)
        {
            try
            {
                var json = JsonSerializer.Serialize(validOverrides);
                await cache.SetStringAsync(RedisCacheKey, json, CacheEntryOptions, ct);
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogRedisSyncWarning(_logger, ex);
            }
        }
    }

    private async Task<IReadOnlyList<SystemParameterDto>> ToDtosAsync(Dictionary<string, SystemParameter> rows, CancellationToken ct)
    {
        var editorIds = rows.Values.Select(r => r.UpdatedBy ?? r.CreatedBy).OfType<Guid>().Distinct().ToList();
        var editors = await db.Accounts.AsNoTracking()
            .Where(a => editorIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Username, ct);

        var current = store.Current;
        return SystemParameterCatalog.All.Select(d =>
        {
            rows.TryGetValue(d.Key, out var row);
            var editorId = row?.UpdatedBy ?? row?.CreatedBy;
            return new SystemParameterDto(
                d.Key,
                d.Get(current),
                d.Min,
                d.Max,
                d.Locked,
                row is null || d.Locked ? null : row.UpdatedAt ?? row.CreatedAt,
                editorId is { } id && !d.Locked && editors.TryGetValue(id, out var name) ? name : null,
                d.ValueType,
                d.Group);
        }).ToList();
    }
}
