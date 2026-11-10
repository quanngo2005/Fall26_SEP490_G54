using System.Globalization;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameter;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class SystemParameterService(AppDbContext db, SystemParameterStore store) : ISystemParameterService
{
    public async Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await LoadRowsAsync(ct);
        Publish(rows);
        return await ToDtosAsync(rows, ct);
    }

    public async Task RefreshAsync(CancellationToken ct = default) => Publish(await LoadRowsAsync(ct));

    public async Task<IReadOnlyList<SystemParameterDto>> UpdateAsync(UpdateSystemParametersDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Changes is not { Count: > 0 })
        {
            throw new ValidationException("Cần ít nhất một tham số để cập nhật.");
        }

        var errors = new List<string>();
        var parsed = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var change in request.Changes)
        {
            var definition = change is null ? null : SystemParameterCatalog.Find(change.Key);
            if (change is null || definition is null)
            {
                errors.Add($"Tham số '{change?.Key}' không tồn tại.");
            }
            else if (definition.Locked)
            {
                errors.Add($"Tham số '{definition.Key}' được cố định theo quy định, không thể thay đổi.");
            }
            else if (!parsed.TryAdd(definition.Key, change.Value))
            {
                errors.Add($"Tham số '{definition.Key}' bị trùng trong yêu cầu.");
            }
            else if (!IsValid(definition, change.Value))
            {
                errors.Add(definition.Max is null
                    ? $"Tham số '{definition.Key}' phải là số nguyên lớn hơn 0."
                    : $"Tham số '{definition.Key}' phải là số nguyên từ {definition.Min:0.##} đến {definition.Max:0.##}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var keys = parsed.Keys.ToList();
        var existing = await db.SystemParameters.Where(p => keys.Contains(p.Key)).ToDictionaryAsync(p => p.Key, ct);
        foreach (var (key, value) in parsed)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            if (existing.TryGetValue(key, out var row))
            {
                row.Value = text;
            }
            else
            {
                db.SystemParameters.Add(new SystemParameter { Key = key, Value = text });
            }
        }

        await db.SaveChangesAsync(ct);

        var rows = await LoadRowsAsync(ct);
        Publish(rows);
        return await ToDtosAsync(rows, ct);
    }

    private async Task<Dictionary<string, SystemParameter>> LoadRowsAsync(CancellationToken ct)
        => await db.SystemParameters.AsNoTracking().ToDictionaryAsync(p => p.Key, ct);

    private void Publish(Dictionary<string, SystemParameter> rows)
    {
        var overrides = new Dictionary<string, decimal>();
        foreach (var (key, row) in rows)
        {
            var definition = SystemParameterCatalog.Find(key);
            if (definition is { Locked: false }
                && decimal.TryParse(row.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                && IsValid(definition, value))
            {
                overrides[key] = value;
            }
        }

        store.Apply(overrides);
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
                editorId is { } id && !d.Locked && editors.TryGetValue(id, out var name) ? name : null);
        }).ToList();
    }

    /// <summary>Whole number inside the catalog range. A stored value failing this is ignored, never trusted.</summary>
    private static bool IsValid(SystemParameterDefinition definition, decimal value)
        => value == decimal.Truncate(value)
            && value >= (definition.Min ?? 0)
            && value <= (definition.Max ?? SystemParameterDefinition.HardMax);
}
