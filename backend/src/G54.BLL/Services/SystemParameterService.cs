using System.Globalization;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameters;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

public sealed class SystemParameterService(IUnitOfWork uow, IAdminAccessService admin, TimeProvider time) : ISystemParameterService
{
    public async Task<IReadOnlyList<SystemParameterDto>> GetAllAsync(Guid accountId, CancellationToken ct)
    {
        await admin.EnsureAdminAsync(accountId, ct);
        return await LoadDtosAsync(ct);
    }

    public async Task<IReadOnlyList<SystemParameterDto>> SaveAsync(Guid accountId, SaveSystemParametersDto request, CancellationToken ct)
    {
        await admin.EnsureAdminAsync(accountId, ct);

        // Validate request
        var changes = request.Changes;
        if (changes is null || changes.Count == 0 || changes.Count > 8)
        {
            throw new ValidationException("Invalid system parameter change: empty or too many changes.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (!SystemParameterKeys.All.Contains(change.Key))
            {
                throw new ValidationException($"Invalid system parameter change: {change.Key}");
            }

            if (!keys.Add(change.Key))
            {
                throw new ValidationException($"Invalid system parameter change: duplicate key {change.Key}");
            }
        }

        // Load entities for update
        var entities = await uow.SystemParameters.GetByKeysForUpdateAsync(keys, ct);
        if (entities.Count != keys.Count)
        {
            var missing = keys.Except(entities.Select(e => e.Key), StringComparer.Ordinal).First();
            throw new ValidationException($"Invalid system parameter change: {missing}");
        }

        var entityDict = entities.ToDictionary(e => e.Key, e => e, StringComparer.Ordinal);

        // Validate and update each change
        var now = time.GetUtcNow();
        foreach (var change in changes)
        {
            var entity = entityDict[change.Key];

            if (entity.IsLocked || entity.NumericValue is null)
            {
                throw new ValidationException($"Invalid system parameter change: {change.Key}");
            }

            if (change.Value < (entity.MinValue ?? long.MinValue) || (entity.MaxValue is not null && change.Value > entity.MaxValue))
            {
                throw new ValidationException($"Invalid system parameter change: {change.Key}");
            }

            if (change.Value != entity.NumericValue)
            {
                var oldValue = entity.NumericValue.Value.ToString(CultureInfo.InvariantCulture);
                var newValue = change.Value.ToString(CultureInfo.InvariantCulture);

                var paramChange = new SystemParameterChange
                {
                    Id = Guid.NewGuid(),
                    Key = change.Key,
                    OldValue = oldValue,
                    NewValue = newValue,
                    ChangedBy = accountId,
                    ChangedAt = now,
                };

                await uow.Repository<SystemParameterChange>().AddAsync(paramChange, ct);

                entity.NumericValue = change.Value;
                entity.Version++;
                entity.UpdatedAt = now;
                entity.UpdatedBy = accountId;
            }
        }

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("System parameters have changed. Reload before saving.");
        }

        return await LoadDtosAsync(ct);
    }

    private async Task<IReadOnlyList<SystemParameterDto>> LoadDtosAsync(CancellationToken ct)
    {
        var parameters = await uow.SystemParameters.GetAllAsync(ct);

        // Get usernames for UpdatedBy display
        var userIds = parameters
            .Where(p => p.UpdatedBy.HasValue)
            .Select(p => p.UpdatedBy!.Value)
            .Distinct()
            .ToList();

        var usernames = await uow.SystemParameters.GetUsernamesAsync(userIds, ct);

        return parameters.Select(p => new SystemParameterDto(
            Key: p.Key,
            Value: p.NumericValue ?? (object)(p.TextValue ?? string.Empty),
            Min: p.MinValue,
            Max: p.MaxValue,
            Locked: p.IsLocked,
            UpdatedAt: p.UpdatedAt ?? p.CreatedAt,
            UpdatedBy: p.UpdatedBy.HasValue && usernames.TryGetValue(p.UpdatedBy.Value, out var username)
                ? username
                : null
        )).ToList();
    }
}
