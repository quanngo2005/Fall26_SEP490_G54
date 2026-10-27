using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.EvaluationProfile;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace G54.BLL.Services;

public sealed class EvaluationProfileService(AppDbContext db, ILogger<EvaluationProfileService> logger)
    : IEvaluationProfileService
{
    private const decimal RequiredTotalWeightPercent = 100m;
    private const int WeightScale = 4;

    private static readonly Action<ILogger, string, Guid, Exception?> LogProfileAudit =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Information,
            new EventId(1, "EvaluationProfileAudit"),
            "EvaluationProfile audit: Action={Action}, EntityId={EntityId}");

    private static readonly Dictionary<EvaluationProfileStatus, EvaluationProfileStatus[]> AllowedTransitions = new()
    {
        [EvaluationProfileStatus.Draft] = [EvaluationProfileStatus.Active, EvaluationProfileStatus.Archived],
        [EvaluationProfileStatus.Active] = [EvaluationProfileStatus.Inactive, EvaluationProfileStatus.Archived],
        [EvaluationProfileStatus.Inactive] = [EvaluationProfileStatus.Active, EvaluationProfileStatus.Archived],
        [EvaluationProfileStatus.Archived] = [],
    };

    public async Task<PagedResult<EvaluationProfileListItemDto>> GetProfilesAsync(
        EvaluationProfileFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var status = EnumApiFormat.ParseEnum<EvaluationProfileStatus>(filter.Status, "status");
        var query = db.EvaluationProfiles.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(p => p.Code.Contains(keyword) || p.Name.Contains(keyword));
        }

        if (status.HasValue)
        {
            var profileStatus = status.Value;
            query = query.Where(p => p.Status == profileStatus);
        }

        if (filter.JobPositionId.HasValue)
        {
            var jobPositionId = filter.JobPositionId.Value;
            query = query.Where(p => p.JobPositionId == jobPositionId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.JobPosition.Code)
            .ThenBy(p => p.Code)
            .ThenByDescending(p => p.Version)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new EvaluationProfileListItemDto(
                p.Id,
                p.Code,
                p.Name,
                p.JobPositionId,
                p.JobPosition.Code,
                p.JobPosition.Title,
                p.Version,
                p.EffectiveFrom,
                p.EffectiveTo,
                p.Status.ToEnumString(),
                p.EvaluationProfileItems.Count,
                p.EvaluationProfileItems.Sum(i => i.WeightPercent),
                p.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<EvaluationProfileListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<EvaluationProfileDto?> GetProfileByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await db.EvaluationProfiles
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new EvaluationProfileDto(
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.JobPositionId,
                p.JobPosition.Code,
                p.JobPosition.Title,
                p.Version,
                p.EffectiveFrom,
                p.EffectiveTo,
                p.Status.ToEnumString(),
                p.PerformanceEvaluations.Any(),
                p.EvaluationProfileItems.Sum(i => i.WeightPercent),
                p.UpdatedAt,
                p.EvaluationProfileItems
                    .OrderBy(i => i.DisplayOrder)
                    .ThenBy(i => i.KpiGroup.Code)
                    .Select(i => new EvaluationProfileItemDto(
                        i.KpiGroupId,
                        i.KpiGroup.Code,
                        i.KpiGroup.Name,
                        i.WeightPercent,
                        i.DisplayOrder))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<EvaluationProfileDto> CreateProfileAsync(
        CreateEvaluationProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(dto.Code);
        var name = NormalizeName(dto.Name);
        var description = NormalizeDescription(dto.Description);
        ValidateEffectivePeriod(dto.EffectiveFrom, dto.EffectiveTo);
        ValidateItems(dto.Items);

        await EnsureJobPositionExistsAsync(dto.JobPositionId, cancellationToken);
        await EnsureKpiGroupsExistAsync(dto.Items, cancellationToken);

        var existingVersion = await db.EvaluationProfiles
            .AsNoTracking()
            .Where(p => p.Code == code)
            .OrderByDescending(p => p.Version)
            .Select(p => new { p.JobPositionId, p.Version })
            .FirstOrDefaultAsync(cancellationToken);
        if (existingVersion is not null && existingVersion.JobPositionId != dto.JobPositionId)
        {
            throw new ConflictException(
                $"Mã hồ sơ đánh giá '{code}' đã thuộc vị trí công việc khác. Phiên bản mới phải cùng vị trí công việc.");
        }

        await EnsurePositionEffectiveFromUniqueAsync(dto.JobPositionId, dto.EffectiveFrom, null, cancellationToken);

        var profile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = dto.JobPositionId,
            Code = code,
            Name = name,
            Description = description,
            Version = (existingVersion?.Version ?? 0) + 1,
            EffectiveFrom = dto.EffectiveFrom,
            EffectiveTo = dto.EffectiveTo,
            Status = EvaluationProfileStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var item in dto.Items)
        {
            profile.EvaluationProfileItems.Add(NewItem(profile.Id, item));
        }

        db.EvaluationProfiles.Add(profile);
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "CREATE_EVALUATION_PROFILE", profile.Id, null);
        return await GetRequiredProfileAsync(profile.Id, cancellationToken);
    }

    public async Task<EvaluationProfileDto> UpdateProfileAsync(
        Guid id,
        UpdateEvaluationProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeName(dto.Name);
        var description = NormalizeDescription(dto.Description);
        ValidateEffectivePeriod(dto.EffectiveFrom, dto.EffectiveTo);
        ValidateItems(dto.Items);

        var profile = await db.EvaluationProfiles
            .Include(p => p.EvaluationProfileItems)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy hồ sơ đánh giá yêu cầu.");

        if (profile.Status == EvaluationProfileStatus.Archived)
        {
            throw new ConflictException("Hồ sơ đánh giá đã lưu trữ (Archived) nên không thể chỉnh sửa.");
        }

        var positionChanged = profile.JobPositionId != dto.JobPositionId;
        var periodChanged = profile.EffectiveFrom != dto.EffectiveFrom || profile.EffectiveTo != dto.EffectiveTo;

        if (positionChanged)
        {
            await EnsureJobPositionExistsAsync(dto.JobPositionId, cancellationToken);
        }

        await EnsureKpiGroupsExistAsync(dto.Items, cancellationToken);

        var isInUse = await db.PerformanceEvaluations.AnyAsync(e => e.EvaluationProfileId == id, cancellationToken);
        if (isInUse && (positionChanged || profile.EffectiveFrom != dto.EffectiveFrom || ItemsDiffer(profile, dto.Items)))
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.EvaluationProfileInUse),
                ErrorCodes.EvaluationProfileInUse);
        }

        if (positionChanged || profile.EffectiveFrom != dto.EffectiveFrom)
        {
            await EnsurePositionEffectiveFromUniqueAsync(dto.JobPositionId, dto.EffectiveFrom, id, cancellationToken);
        }

        if (profile.Status == EvaluationProfileStatus.Active)
        {
            EnsureTotalWeightIsComplete(dto.Items.Sum(i => i.WeightPercent));

            if (positionChanged || periodChanged)
            {
                await EnsureNoOverlappingActiveProfileAsync(
                    dto.JobPositionId, profile.Code, profile.Id, dto.EffectiveFrom, dto.EffectiveTo, cancellationToken);
            }
        }

        profile.Name = name;
        profile.Description = description;
        profile.JobPositionId = dto.JobPositionId;
        profile.EffectiveFrom = dto.EffectiveFrom;
        profile.EffectiveTo = dto.EffectiveTo;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        ReplaceItems(profile, dto.Items);

        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "UPDATE_EVALUATION_PROFILE", id, null);
        return await GetRequiredProfileAsync(id, cancellationToken);
    }

    public async Task ChangeStatusAsync(
        Guid id,
        EvaluationProfileStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var target = EnumApiFormat.ParseRequiredEnum<EvaluationProfileStatus>(dto.Status, "status");

        var profile = await db.EvaluationProfiles
            .Include(p => p.EvaluationProfileItems)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy hồ sơ đánh giá yêu cầu.");

        if (profile.Status == target)
        {
            return;
        }

        if (!AllowedTransitions[profile.Status].Contains(target))
        {
            throw new ConflictException(
                $"Không thể chuyển hồ sơ đánh giá từ {profile.Status.ToEnumString()} sang {target.ToEnumString()}.");
        }

        if (target == EvaluationProfileStatus.Active)
        {
            await ActivateAsync(profile, cancellationToken);
        }
        else
        {
            profile.Status = target;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveChangesAsync(cancellationToken);
        }

        LogProfileAudit(logger, "CHANGE_EVALUATION_PROFILE_STATUS", id, null);
    }

    public async Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var profile = await db.EvaluationProfiles.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy hồ sơ đánh giá yêu cầu.");

        if (profile.Status == EvaluationProfileStatus.Archived)
        {
            return;
        }

        profile.Status = EvaluationProfileStatus.Archived;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "ARCHIVE_EVALUATION_PROFILE", id, null);
    }

    public async Task<List<EvaluationProfileItemDto>?> GetItemsAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.EvaluationProfiles.AnyAsync(p => p.Id == profileId, cancellationToken))
        {
            return null;
        }

        return await db.EvaluationProfileItems
            .AsNoTracking()
            .Where(i => i.EvaluationProfileId == profileId)
            .OrderBy(i => i.DisplayOrder)
            .ThenBy(i => i.KpiGroup.Code)
            .Select(i => new EvaluationProfileItemDto(
                i.KpiGroupId,
                i.KpiGroup.Code,
                i.KpiGroup.Name,
                i.WeightPercent,
                i.DisplayOrder))
            .ToListAsync(cancellationToken);
    }

    public async Task<EvaluationProfileItemDto> AddItemAsync(
        Guid profileId,
        EvaluationProfileItemInputDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateItem(dto);

        var profile = await GetEditableProfileAsync(profileId, cancellationToken);
        await EnsureKpiGroupsExistAsync([dto], cancellationToken);

        var alreadyAdded = await db.EvaluationProfileItems.AnyAsync(
            i => i.EvaluationProfileId == profileId && i.KpiGroupId == dto.KpiGroupId,
            cancellationToken);
        if (alreadyAdded)
        {
            throw new ConflictException("Nhóm KPI này đã có trong hồ sơ đánh giá.");
        }

        db.EvaluationProfileItems.Add(NewItem(profileId, dto));
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "ADD_EVALUATION_PROFILE_ITEM", profileId, null);
        return await GetRequiredItemAsync(profileId, dto.KpiGroupId, cancellationToken);
    }

    public async Task<EvaluationProfileItemDto> UpdateItemAsync(
        Guid profileId,
        Guid kpiGroupId,
        UpdateEvaluationProfileItemDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateItemValues(dto.WeightPercent, dto.DisplayOrder);

        var profile = await GetEditableProfileAsync(profileId, cancellationToken);
        var item = await FindItemAsync(profileId, kpiGroupId, cancellationToken);

        item.WeightPercent = dto.WeightPercent;
        item.DisplayOrder = dto.DisplayOrder;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = item.UpdatedAt;
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "UPDATE_EVALUATION_PROFILE_ITEM", profileId, null);
        return await GetRequiredItemAsync(profileId, kpiGroupId, cancellationToken);
    }

    public async Task RemoveItemAsync(Guid profileId, Guid kpiGroupId, CancellationToken cancellationToken = default)
    {
        var profile = await GetEditableProfileAsync(profileId, cancellationToken);
        var item = await FindItemAsync(profileId, kpiGroupId, cancellationToken);

        db.EvaluationProfileItems.Remove(item);
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "REMOVE_EVALUATION_PROFILE_ITEM", profileId, null);
    }

    /// <summary>
    /// Item-by-item changes are allowed only while the total weight is not enforced (DRAFT, INACTIVE). An ACTIVE
    /// profile must keep its total at the required value, so it is changed as a whole through the profile update.
    /// </summary>
    private async Task<EvaluationProfile> GetEditableProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await db.EvaluationProfiles.SingleOrDefaultAsync(p => p.Id == profileId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy hồ sơ đánh giá yêu cầu.");

        if (profile.Status == EvaluationProfileStatus.Archived)
        {
            throw new ConflictException("Hồ sơ đánh giá đã lưu trữ (Archived) nên không thể chỉnh sửa.");
        }

        if (await db.PerformanceEvaluations.AnyAsync(e => e.EvaluationProfileId == profileId, cancellationToken))
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.EvaluationProfileInUse),
                ErrorCodes.EvaluationProfileInUse);
        }

        if (profile.Status == EvaluationProfileStatus.Active)
        {
            throw new ConflictException(
                "Hồ sơ đánh giá đang hoạt động (Active) phải giữ tổng trọng số hợp lệ nên chỉ được đổi toàn bộ danh sách nhóm KPI qua cập nhật hồ sơ.");
        }

        return profile;
    }

    private async Task<EvaluationProfileItem> FindItemAsync(
        Guid profileId,
        Guid kpiGroupId,
        CancellationToken cancellationToken)
    {
        return await db.EvaluationProfileItems.SingleOrDefaultAsync(
                i => i.EvaluationProfileId == profileId && i.KpiGroupId == kpiGroupId,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nhóm KPI trong hồ sơ đánh giá.");
    }

    private async Task<EvaluationProfileItemDto> GetRequiredItemAsync(
        Guid profileId,
        Guid kpiGroupId,
        CancellationToken cancellationToken)
    {
        return await db.EvaluationProfileItems
            .AsNoTracking()
            .Where(i => i.EvaluationProfileId == profileId && i.KpiGroupId == kpiGroupId)
            .Select(i => new EvaluationProfileItemDto(
                i.KpiGroupId,
                i.KpiGroup.Code,
                i.KpiGroup.Name,
                i.WeightPercent,
                i.DisplayOrder))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nhóm KPI trong hồ sơ đánh giá.");
    }

    /// <summary>
    /// The unique index allows one ACTIVE profile per code, and EF may order the updates of a single save freely,
    /// so the previous ACTIVE version is retired and saved before the new one is activated, inside one transaction.
    /// </summary>
    private async Task ActivateAsync(EvaluationProfile profile, CancellationToken cancellationToken)
    {
        EnsureHasItems(profile);
        EnsureTotalWeightIsComplete(profile.EvaluationProfileItems.Sum(i => i.WeightPercent));

        var position = await db.JobPositions
            .AsNoTracking()
            .Where(p => p.Id == profile.JobPositionId)
            .Select(p => new { p.Status })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy vị trí công việc yêu cầu.");
        if (position.Status != RecordStatus.Active)
        {
            throw new ValidationException("Vị trí công việc của hồ sơ đánh giá không ở trạng thái hoạt động (Active).");
        }

        await EnsureNoOverlappingActiveProfileAsync(
            profile.JobPositionId, profile.Code, profile.Id, profile.EffectiveFrom, profile.EffectiveTo, cancellationToken);

        var supersededVersions = await db.EvaluationProfiles
            .Where(p => p.Code == profile.Code && p.Id != profile.Id && p.Status == EvaluationProfileStatus.Active)
            .ToListAsync(cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        if (supersededVersions.Count > 0)
        {
            foreach (var superseded in supersededVersions)
            {
                superseded.Status = EvaluationProfileStatus.Inactive;
                superseded.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await SaveChangesAsync(cancellationToken);
        }

        profile.Status = EvaluationProfileStatus.Active;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        // The in-memory provider used by unit tests has no transaction support.
        return db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Hồ sơ đánh giá đã bị thay đổi hoặc trùng dữ liệu. Vui lòng tải lại và thử lại.");
        }
    }

    private async Task EnsureJobPositionExistsAsync(Guid jobPositionId, CancellationToken cancellationToken)
    {
        if (jobPositionId == Guid.Empty
            || !await db.JobPositions.AnyAsync(p => p.Id == jobPositionId, cancellationToken))
        {
            throw new NotFoundException("Không tìm thấy vị trí công việc yêu cầu.");
        }
    }

    private async Task EnsureKpiGroupsExistAsync(
        List<EvaluationProfileItemInputDto> items,
        CancellationToken cancellationToken)
    {
        var groupIds = items.Select(i => i.KpiGroupId).Distinct().ToList();
        if (groupIds.Count == 0)
        {
            return;
        }

        var existing = await db.KpiGroups.CountAsync(g => groupIds.Contains(g.Id), cancellationToken);
        if (existing != groupIds.Count)
        {
            throw new NotFoundException("Một hoặc nhiều nhóm KPI được chọn không tồn tại.");
        }
    }

    private async Task EnsurePositionEffectiveFromUniqueAsync(
        Guid jobPositionId,
        DateOnly effectiveFrom,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var exists = await db.EvaluationProfiles.AnyAsync(
            p => p.JobPositionId == jobPositionId
                && p.EffectiveFrom == effectiveFrom
                && (!excludeId.HasValue || p.Id != excludeId.Value),
            cancellationToken);
        if (exists)
        {
            throw new ConflictException(
                $"Vị trí công việc đã có hồ sơ đánh giá hiệu lực từ ngày {effectiveFrom:yyyy-MM-dd}.");
        }
    }

    /// <summary>Other versions of the same code are ignored: activation retires the ACTIVE one.</summary>
    private async Task EnsureNoOverlappingActiveProfileAsync(
        Guid jobPositionId,
        string code,
        Guid profileId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        CancellationToken cancellationToken)
    {
        var overlaps = await db.EvaluationProfiles.AnyAsync(
            p => p.Id != profileId
                && p.Code != code
                && p.JobPositionId == jobPositionId
                && p.Status == EvaluationProfileStatus.Active
                && (effectiveTo == null || p.EffectiveFrom <= effectiveTo)
                && (p.EffectiveTo == null || p.EffectiveTo >= effectiveFrom),
            cancellationToken);
        if (overlaps)
        {
            throw new ConflictException(
                "Khoảng hiệu lực chồng với một hồ sơ đánh giá đang hoạt động (Active) khác của cùng vị trí công việc.");
        }
    }

    private static bool ItemsDiffer(EvaluationProfile profile, List<EvaluationProfileItemInputDto> items)
    {
        if (profile.EvaluationProfileItems.Count != items.Count)
        {
            return true;
        }

        var current = profile.EvaluationProfileItems.ToDictionary(i => i.KpiGroupId);
        return items.Any(i =>
            !current.TryGetValue(i.KpiGroupId, out var existing)
            || existing.WeightPercent != i.WeightPercent
            || existing.DisplayOrder != i.DisplayOrder);
    }

    /// <summary>Updates matching items in place, removes missing ones and adds new ones.</summary>
    private void ReplaceItems(EvaluationProfile profile, List<EvaluationProfileItemInputDto> items)
    {
        var requested = items.ToDictionary(i => i.KpiGroupId);
        var current = profile.EvaluationProfileItems.ToDictionary(i => i.KpiGroupId);

        foreach (var (groupId, existing) in current)
        {
            if (!requested.ContainsKey(groupId))
            {
                db.EvaluationProfileItems.Remove(existing);
            }
        }

        foreach (var (groupId, input) in requested)
        {
            if (current.TryGetValue(groupId, out var existing))
            {
                if (existing.WeightPercent != input.WeightPercent || existing.DisplayOrder != input.DisplayOrder)
                {
                    existing.WeightPercent = input.WeightPercent;
                    existing.DisplayOrder = input.DisplayOrder;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                db.EvaluationProfileItems.Add(NewItem(profile.Id, input));
            }
        }
    }

    private static EvaluationProfileItem NewItem(Guid profileId, EvaluationProfileItemInputDto input) => new()
    {
        Id = Guid.NewGuid(),
        EvaluationProfileId = profileId,
        KpiGroupId = input.KpiGroupId,
        WeightPercent = input.WeightPercent,
        DisplayOrder = input.DisplayOrder,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<EvaluationProfileDto> GetRequiredProfileAsync(Guid id, CancellationToken cancellationToken)
    {
        return await GetProfileByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy hồ sơ đánh giá yêu cầu.");
    }

    private static void EnsureHasItems(EvaluationProfile profile)
    {
        if (profile.EvaluationProfileItems.Count == 0)
        {
            throw new ValidationException("Hồ sơ đánh giá phải có ít nhất một nhóm KPI trước khi kích hoạt.");
        }
    }

    private static void EnsureTotalWeightIsComplete(decimal totalWeightPercent)
    {
        if (totalWeightPercent != RequiredTotalWeightPercent)
        {
            throw new ValidationException(
                $"Tổng trọng số các nhóm KPI phải bằng đúng {RequiredTotalWeightPercent:0}% (hiện tại {totalWeightPercent:0.####}%).");
        }
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
        {
            throw new ValidationException("Mã hồ sơ đánh giá là bắt buộc và tối đa 50 ký tự.");
        }

        return code.Trim();
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ValidationException("Tên hồ sơ đánh giá là bắt buộc và tối đa 200 ký tự.");
        }

        return name.Trim();
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        if (description.Trim().Length > 2000)
        {
            throw new ValidationException("Mô tả hồ sơ đánh giá tối đa 2000 ký tự.");
        }

        return description.Trim();
    }

    private static void ValidateEffectivePeriod(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveFrom == default)
        {
            throw new ValidationException("Ngày bắt đầu hiệu lực là bắt buộc.");
        }

        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
        {
            throw new ValidationException(
                $"Ngày kết thúc ({effectiveTo:yyyy-MM-dd}) không được nhỏ hơn ngày bắt đầu ({effectiveFrom:yyyy-MM-dd}).");
        }
    }

    private static void ValidateItems(List<EvaluationProfileItemInputDto>? items)
    {
        if (items is null)
        {
            throw new ValidationException("Danh sách nhóm KPI là bắt buộc.");
        }

        var duplicate = items.GroupBy(i => i.KpiGroupId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ValidationException($"Danh sách nhóm KPI chứa nhóm trùng lặp (ID: {duplicate.Key}).");
        }

        foreach (var item in items)
        {
            ValidateItem(item);
        }
    }

    private static void ValidateItem(EvaluationProfileItemInputDto item)
    {
        if (item.KpiGroupId == Guid.Empty)
        {
            throw new ValidationException("KpiGroupId không được để trống.");
        }

        ValidateItemValues(item.WeightPercent, item.DisplayOrder);
    }

    private static void ValidateItemValues(decimal weightPercent, int displayOrder)
    {
        if (weightPercent <= 0 || weightPercent > RequiredTotalWeightPercent)
        {
            throw new ValidationException(
                $"Trọng số của mỗi nhóm KPI phải lớn hơn 0 và không vượt quá {RequiredTotalWeightPercent:0}.");
        }

        if (decimal.Round(weightPercent, WeightScale) != weightPercent)
        {
            throw new ValidationException($"Trọng số tối đa {WeightScale} chữ số thập phân.");
        }

        if (displayOrder < 1)
        {
            throw new ValidationException("Thứ tự hiển thị phải từ 1 trở lên.");
        }
    }
}
