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
    private const int MaxTreeDepth = 3;

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
                p.EvaluationProfileItems.Count(i => i.ParentItemId == null),
                p.EvaluationProfileItems.Where(i => i.ParentItemId == null).Sum(i => i.WeightPercent),
                p.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<EvaluationProfileListItemDto>(items, totalCount, page, pageSize);
    }

    public async Task<EvaluationProfileDto?> GetProfileByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var profile = await db.EvaluationProfiles
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
                p.PerformanceEvaluations.Count != 0,
                p.EvaluationProfileItems.Where(i => i.ParentItemId == null).Sum(i => i.WeightPercent),
                p.UpdatedAt,
                new List<EvaluationProfileItemDto>()))
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
        {
            return null;
        }

        return profile with { Items = await LoadItemTreeAsync(id, cancellationToken) };
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
        await EnsureKpiDefinitionsExistAsync(dto.Items, cancellationToken);

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

        foreach (var item in FlattenItems(profile.Id, null, dto.Items))
        {
            profile.EvaluationProfileItems.Add(item);
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

        await EnsureKpiDefinitionsExistAsync(dto.Items, cancellationToken);

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
            EnsureTotalWeightIsComplete(dto.Items);

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

        return await LoadItemTreeAsync(profileId, cancellationToken);
    }

    public async Task<EvaluationProfileItemDto> AddItemAsync(
        Guid profileId,
        AddEvaluationProfileItemDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateNodeShape(dto.Name, dto.KpiDefinitionId, dto.WeightPercent, dto.DisplayOrder);

        var profile = await GetEditableProfileAsync(profileId, cancellationToken);
        var items = await db.EvaluationProfileItems.Where(i => i.EvaluationProfileId == profileId).ToListAsync(cancellationToken);

        var depth = 1;
        if (dto.ParentItemId.HasValue)
        {
            var parent = items.SingleOrDefault(i => i.Id == dto.ParentItemId.Value)
                ?? throw new NotFoundException("Không tìm thấy nút cha trong hồ sơ đánh giá.");
            if (parent.KpiDefinitionId.HasValue)
            {
                throw new ValidationException("Nút cha phải là một nhóm, không phải một KPI.");
            }

            depth = DepthOf(items, parent) + 1;
        }
        else if (dto.KpiDefinitionId.HasValue)
        {
            throw new ValidationException("Nút gốc phải là một nhóm KPI, không phải một KPI.");
        }

        if (depth > MaxTreeDepth)
        {
            throw new ValidationException($"Cây trọng số tối đa {MaxTreeDepth} cấp.");
        }

        if (!dto.KpiDefinitionId.HasValue)
        {
            EnsureSiblingNameIsFree(items, dto.ParentItemId, dto.Name!, null);
        }

        if (dto.KpiDefinitionId.HasValue)
        {
            await EnsureKpiDefinitionsExistAsync(
                [new EvaluationProfileItemInputDto { KpiDefinitionId = dto.KpiDefinitionId }], cancellationToken);
            if (items.Any(i => i.KpiDefinitionId == dto.KpiDefinitionId))
            {
                throw new ConflictException("KPI này đã có trong hồ sơ đánh giá.");
            }
        }

        var item = new EvaluationProfileItem
        {
            Id = Guid.NewGuid(),
            EvaluationProfileId = profileId,
            ParentItemId = dto.ParentItemId,
            KpiDefinitionId = dto.KpiDefinitionId,
            Name = dto.KpiDefinitionId.HasValue ? null : dto.Name!.Trim(),
            WeightPercent = dto.WeightPercent,
            DisplayOrder = dto.DisplayOrder,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        db.EvaluationProfileItems.Add(item);
        profile.UpdatedAt = item.UpdatedAt;
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "ADD_EVALUATION_PROFILE_ITEM", profileId, null);
        return await GetRequiredItemAsync(profileId, item.Id, cancellationToken);
    }

    public async Task<EvaluationProfileItemDto> UpdateItemAsync(
        Guid profileId,
        Guid itemId,
        UpdateEvaluationProfileItemDto dto,
        CancellationToken cancellationToken = default)
    {
        ValidateItemValues(dto.WeightPercent, dto.DisplayOrder);

        var profile = await GetEditableProfileAsync(profileId, cancellationToken);
        var item = await FindItemAsync(profileId, itemId, cancellationToken);

        if (dto.Name is not null)
        {
            if (item.KpiDefinitionId.HasValue)
            {
                throw new ValidationException("Chỉ nhóm KPI mới có tên; KPI lấy tên từ danh mục KPI.");
            }

            var siblings = await db.EvaluationProfileItems
                .Where(i => i.EvaluationProfileId == profileId)
                .ToListAsync(cancellationToken);
            EnsureSiblingNameIsFree(siblings, item.ParentItemId, dto.Name, item.Id);
            item.Name = NormalizeNodeName(dto.Name);
        }

        item.WeightPercent = dto.WeightPercent;
        item.DisplayOrder = dto.DisplayOrder;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = item.UpdatedAt;
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "UPDATE_EVALUATION_PROFILE_ITEM", profileId, null);
        return await GetRequiredItemAsync(profileId, itemId, cancellationToken);
    }

    public async Task RemoveItemAsync(Guid profileId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var profile = await GetEditableProfileAsync(profileId, cancellationToken);
        var items = await db.EvaluationProfileItems.Where(i => i.EvaluationProfileId == profileId).ToListAsync(cancellationToken);
        var item = items.SingleOrDefault(i => i.Id == itemId)
            ?? throw new NotFoundException("Không tìm thấy nút trong hồ sơ đánh giá.");

        // The self reference does not cascade in the database, so the subtree is removed explicitly, leaves first.
        var childrenOf = items.Where(i => i.ParentItemId.HasValue).ToLookup(i => i.ParentItemId!.Value);
        var subtree = new List<EvaluationProfileItem>();
        CollectSubtree(item, childrenOf, subtree);
        db.EvaluationProfileItems.RemoveRange(subtree);

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveChangesAsync(cancellationToken);

        LogProfileAudit(logger, "REMOVE_EVALUATION_PROFILE_ITEM", profileId, null);
    }

    /// <summary>
    /// Item-by-item changes are allowed only while the weights are not enforced (DRAFT, INACTIVE). An ACTIVE
    /// profile must keep every sibling group at 100, so it is changed as a whole through the profile update.
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
                "Hồ sơ đánh giá đang hoạt động (Active) phải giữ trọng số hợp lệ nên chỉ được đổi toàn bộ cây trọng số qua cập nhật hồ sơ.");
        }

        return profile;
    }

    private async Task<EvaluationProfileItem> FindItemAsync(
        Guid profileId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        return await db.EvaluationProfileItems.SingleOrDefaultAsync(
                i => i.EvaluationProfileId == profileId && i.Id == itemId,
                cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nút trong hồ sơ đánh giá.");
    }

    private async Task<EvaluationProfileItemDto> GetRequiredItemAsync(
        Guid profileId,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var tree = await LoadItemTreeAsync(profileId, cancellationToken);
        return FindInTree(tree, itemId)
            ?? throw new NotFoundException("Không tìm thấy nút trong hồ sơ đánh giá.");
    }

    /// <summary>The profile's items as a tree: root groups in display order, each with its children.</summary>
    private async Task<List<EvaluationProfileItemDto>> LoadItemTreeAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var items = await db.EvaluationProfileItems
            .AsNoTracking()
            .Where(i => i.EvaluationProfileId == profileId)
            .Include(i => i.KpiDefinition)
            .ToListAsync(cancellationToken);

        var childrenOf = items.Where(i => i.ParentItemId.HasValue).ToLookup(i => i.ParentItemId!.Value);
        return items
            .Where(i => !i.ParentItemId.HasValue)
            .OrderBy(i => i.DisplayOrder)
            .Select(i => ToDto(i, childrenOf))
            .ToList();
    }

    private static EvaluationProfileItemDto ToDto(EvaluationProfileItem item, ILookup<Guid, EvaluationProfileItem> childrenOf) =>
        new(
            item.Id,
            item.ParentItemId,
            item.Name,
            item.KpiDefinitionId,
            item.KpiDefinition?.Code,
            item.KpiDefinition?.Name,
            item.WeightPercent,
            item.DisplayOrder,
            childrenOf[item.Id]
                .OrderBy(child => child.DisplayOrder)
                .Select(child => ToDto(child, childrenOf))
                .ToList());

    private static EvaluationProfileItemDto? FindInTree(IEnumerable<EvaluationProfileItemDto> nodes, Guid id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
            {
                return node;
            }

            var found = FindInTree(node.Children, id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static void CollectSubtree(
        EvaluationProfileItem item,
        ILookup<Guid, EvaluationProfileItem> childrenOf,
        List<EvaluationProfileItem> result)
    {
        foreach (var child in childrenOf[item.Id])
        {
            CollectSubtree(child, childrenOf, result);
        }

        result.Add(item);
    }

    /// <summary>Sibling groups are told apart by name (case-insensitive), so a name may appear once under a parent.</summary>
    private static void EnsureSiblingNameIsFree(
        List<EvaluationProfileItem> items,
        Guid? parentItemId,
        string name,
        Guid? exceptItemId)
    {
        var wanted = NormalizeNodeName(name);
        var taken = items.Any(i => i.ParentItemId == parentItemId
            && i.Id != exceptItemId
            && !i.KpiDefinitionId.HasValue
            && string.Equals(i.Name?.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
        if (taken)
        {
            throw new ConflictException($"Đã có nhóm tên '{name.Trim()}' cùng cấp trong hồ sơ đánh giá.");
        }
    }

    private static int DepthOf(List<EvaluationProfileItem> items, EvaluationProfileItem item)
    {
        var depth = 1;
        var current = item;
        var seen = new HashSet<Guid> { item.Id };
        while (current.ParentItemId.HasValue)
        {
            current = items.Single(i => i.Id == current.ParentItemId!.Value);
            if (!seen.Add(current.Id))
            {
                break;
            }

            depth++;
        }

        return depth;
    }
    /// <summary>
    /// The unique index allows one ACTIVE profile per code, and EF may order the updates of a single save freely,
    /// so the previous ACTIVE version is retired and saved before the new one is activated, inside one transaction.
    /// </summary>
    private async Task ActivateAsync(EvaluationProfile profile, CancellationToken cancellationToken)
    {
        EnsureHasItems(profile);
        EnsureTreeIsComplete(profile.EvaluationProfileItems);

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

    private async Task EnsureKpiDefinitionsExistAsync(
        List<EvaluationProfileItemInputDto> items,
        CancellationToken cancellationToken)
    {
        var kpiIds = Descendants(items)
            .Where(i => i.KpiDefinitionId.HasValue)
            .Select(i => i.KpiDefinitionId!.Value)
            .Distinct()
            .ToList();
        if (kpiIds.Count == 0)
        {
            return;
        }

        var existing = await db.KpiDefinitions.CountAsync(k => kpiIds.Contains(k.Id), cancellationToken);
        if (existing != kpiIds.Count)
        {
            throw new NotFoundException("Một hoặc nhiều KPI được chọn không tồn tại.");
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

    /// <summary>Compares the stored tree with the requested one, ignoring ids (the request carries none).</summary>
    private static bool ItemsDiffer(EvaluationProfile profile, List<EvaluationProfileItemInputDto> items)
    {
        var childrenOf = profile.EvaluationProfileItems
            .Where(i => i.ParentItemId.HasValue)
            .ToLookup(i => i.ParentItemId!.Value);
        var roots = profile.EvaluationProfileItems.Where(i => !i.ParentItemId.HasValue).ToList();

        return !SameLevel(roots, items, childrenOf);
    }

    private static bool SameLevel(
        List<EvaluationProfileItem> stored,
        List<EvaluationProfileItemInputDto> requested,
        ILookup<Guid, EvaluationProfileItem> childrenOf)
    {
        if (stored.Count != requested.Count)
        {
            return false;
        }

        var storedByKey = stored.ToDictionary(NodeKey);
        foreach (var input in requested)
        {
            if (!storedByKey.TryGetValue(NodeKey(input), out var existing)
                || existing.WeightPercent != input.WeightPercent
                || existing.DisplayOrder != input.DisplayOrder
                || (!existing.KpiDefinitionId.HasValue && existing.Name != input.Name?.Trim())
                || !SameLevel(childrenOf[existing.Id].ToList(), input.Children, childrenOf))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A leaf is identified by its KPI, a group by its name (case-insensitive) among its siblings.</summary>
    private static string NodeKey(EvaluationProfileItem item) =>
        item.KpiDefinitionId.HasValue ? $"kpi:{item.KpiDefinitionId}" : $"group:{item.Name?.Trim().ToUpperInvariant()}";

    private static string NodeKey(EvaluationProfileItemInputDto input) =>
        input.KpiDefinitionId.HasValue ? $"kpi:{input.KpiDefinitionId}" : $"group:{input.Name?.Trim().ToUpperInvariant()}";

    /// <summary>
    /// Brings the stored tree to the requested one. A node is matched with its stored counterpart by its key (the KPI of
    /// a leaf, the name of a group) among the same parent: matches are updated in place and keep their id, nodes that
    /// are no longer requested are removed with everything beneath them, and new nodes are added.
    /// </summary>
    private void ReplaceItems(EvaluationProfile profile, List<EvaluationProfileItemInputDto> items)
    {
        var childrenOf = profile.EvaluationProfileItems
            .Where(i => i.ParentItemId.HasValue)
            .ToLookup(i => i.ParentItemId!.Value);
        var roots = profile.EvaluationProfileItems.Where(i => !i.ParentItemId.HasValue).ToList();

        SyncLevel(profile.Id, null, roots, items, childrenOf);
    }

    private void SyncLevel(
        Guid profileId,
        Guid? parentId,
        List<EvaluationProfileItem> stored,
        List<EvaluationProfileItemInputDto> requested,
        ILookup<Guid, EvaluationProfileItem> childrenOf)
    {
        var storedByKey = stored.ToDictionary(NodeKey);
        var requestedKeys = requested.Select(NodeKey).ToHashSet();

        // The self reference does not cascade in the database: a removed node goes with its subtree, children first.
        foreach (var gone in stored.Where(item => !requestedKeys.Contains(NodeKey(item))))
        {
            var removed = new List<EvaluationProfileItem>();
            CollectSubtree(gone, childrenOf, removed);
            db.EvaluationProfileItems.RemoveRange(removed);
        }

        foreach (var input in requested)
        {
            if (!storedByKey.TryGetValue(NodeKey(input), out var existing))
            {
                foreach (var added in FlattenItems(profileId, parentId, [input]))
                {
                    db.EvaluationProfileItems.Add(added);
                }

                continue;
            }

            var name = input.KpiDefinitionId.HasValue ? null : input.Name!.Trim();
            if (existing.WeightPercent != input.WeightPercent
                || existing.DisplayOrder != input.DisplayOrder
                || existing.Name != name)
            {
                existing.WeightPercent = input.WeightPercent;
                existing.DisplayOrder = input.DisplayOrder;
                existing.Name = name;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }

            if (!existing.KpiDefinitionId.HasValue)
            {
                SyncLevel(profileId, existing.Id, childrenOf[existing.Id].ToList(), input.Children, childrenOf);
            }
        }
    }
    /// <summary>Turns the requested tree into flat entities with generated ids and parent links.</summary>
    private static IEnumerable<EvaluationProfileItem> FlattenItems(
        Guid profileId,
        Guid? parentId,
        List<EvaluationProfileItemInputDto> inputs)
    {
        foreach (var input in inputs)
        {
            var item = new EvaluationProfileItem
            {
                Id = Guid.NewGuid(),
                EvaluationProfileId = profileId,
                ParentItemId = parentId,
                KpiDefinitionId = input.KpiDefinitionId,
                Name = input.KpiDefinitionId.HasValue ? null : input.Name!.Trim(),
                WeightPercent = input.WeightPercent,
                DisplayOrder = input.DisplayOrder,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            yield return item;

            foreach (var child in FlattenItems(profileId, item.Id, input.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<EvaluationProfileItemInputDto> Descendants(IEnumerable<EvaluationProfileItemInputDto> inputs) =>
        inputs.SelectMany(i => new[] { i }.Concat(Descendants(i.Children)));
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

    /// <summary>Activation rules on the stored tree: see <see cref="EnsureTreeShape"/>.</summary>
    private static void EnsureTreeIsComplete(IEnumerable<EvaluationProfileItem> items)
    {
        var all = items.ToList();
        var childrenOf = all.Where(i => i.ParentItemId.HasValue).ToLookup(i => i.ParentItemId!.Value);

        if (all.Any(i => !i.ParentItemId.HasValue && i.KpiDefinitionId.HasValue))
        {
            throw new ValidationException("Nút gốc phải là một nhóm KPI, không phải một KPI.");
        }

        EnsureSiblingsTotal(all.Where(i => !i.ParentItemId.HasValue).Sum(i => i.WeightPercent), "gốc");
        foreach (var group in all.Where(i => !i.KpiDefinitionId.HasValue))
        {
            var children = childrenOf[group.Id].ToList();
            if (children.Count == 0)
            {
                throw new ValidationException($"Nhóm '{group.Name}' phải có ít nhất một nhóm con hoặc KPI.");
            }

            EnsureSiblingsTotal(children.Sum(i => i.WeightPercent), $"nhóm '{group.Name}'");
        }
    }

    private static void EnsureTotalWeightIsComplete(List<EvaluationProfileItemInputDto> items)
    {
        EnsureSiblingsTotal(items.Sum(i => i.WeightPercent), "gốc");
        foreach (var group in Descendants(items).Where(i => !i.KpiDefinitionId.HasValue))
        {
            EnsureSiblingsTotal(group.Children.Sum(i => i.WeightPercent), $"nhóm '{group.Name}'");
        }
    }

    private static void EnsureSiblingsTotal(decimal totalWeightPercent, string where)
    {
        if (totalWeightPercent != RequiredTotalWeightPercent)
        {
            throw new ValidationException(
                $"Tổng trọng số các nút cùng cấp dưới {where} phải bằng đúng {RequiredTotalWeightPercent:0}% (hiện tại {totalWeightPercent:0.####}%).");
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

    /// <summary>
    /// Shape of a requested tree: roots and sub-nodes are groups with children or KPI leaves without any, a KPI
    /// appears once, siblings share no display order or name, and the tree has at most <see cref="MaxTreeDepth"/> levels.
    /// The sibling totals are enforced on activation only, so a draft may be saved half done.
    /// </summary>
    private static void ValidateItems(List<EvaluationProfileItemInputDto>? items)
    {
        if (items is null)
        {
            throw new ValidationException("Danh sách nhóm KPI là bắt buộc.");
        }

        EnsureTreeShape(items, 1);

        var duplicate = Descendants(items)
            .Where(i => i.KpiDefinitionId.HasValue)
            .GroupBy(i => i.KpiDefinitionId!.Value)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ValidationException($"Danh sách chứa KPI trùng lặp (ID: {duplicate.Key}).");
        }
    }

    private static void EnsureTreeShape(List<EvaluationProfileItemInputDto> level, int depth)
    {
        if (depth > MaxTreeDepth)
        {
            throw new ValidationException($"Cây trọng số tối đa {MaxTreeDepth} cấp.");
        }

        var duplicateName = level
            .Where(i => !i.KpiDefinitionId.HasValue)
            .GroupBy(i => i.Name?.Trim().ToUpperInvariant())
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateName is not null)
        {
            throw new ValidationException($"Các nhóm cùng cấp không được trùng tên ('{duplicateName.First().Name}').");
        }

        foreach (var item in level)
        {
            ValidateNodeShape(item.Name, item.KpiDefinitionId, item.WeightPercent, item.DisplayOrder);

            if (item.KpiDefinitionId.HasValue)
            {
                if (depth == 1)
                {
                    throw new ValidationException("Nút gốc phải là một nhóm KPI, không phải một KPI.");
                }

                if (item.Children.Count > 0)
                {
                    throw new ValidationException("Một KPI không được có nút con.");
                }
            }
            else
            {
                EnsureTreeShape(item.Children, depth + 1);
            }
        }
    }

    /// <summary>A node is a group (a name) or a KPI leaf (a KPI), never both and never neither.</summary>
    private static void ValidateNodeShape(string? name, Guid? kpiDefinitionId, decimal weightPercent, int displayOrder)
    {
        var hasName = !string.IsNullOrWhiteSpace(name);
        if (hasName == (kpiDefinitionId.HasValue && kpiDefinitionId.Value != Guid.Empty) || (!hasName && !kpiDefinitionId.HasValue))
        {
            throw new ValidationException("Mỗi nút phải là một nhóm (có tên) hoặc một KPI (có KpiDefinitionId), không phải cả hai.");
        }

        if (kpiDefinitionId == Guid.Empty)
        {
            throw new ValidationException("KpiDefinitionId không được để trống.");
        }

        if (hasName)
        {
            NormalizeNodeName(name!);
        }

        ValidateItemValues(weightPercent, displayOrder);
    }

    private static string NormalizeNodeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new ValidationException("Tên nhóm KPI là bắt buộc và tối đa 200 ký tự.");
        }

        return name.Trim();
    }
    private static void ValidateItemValues(decimal weightPercent, int displayOrder)
    {
        if (weightPercent <= 0 || weightPercent > RequiredTotalWeightPercent)
        {
            throw new ValidationException(
                $"Trọng số của mỗi nút phải lớn hơn 0 và không vượt quá {RequiredTotalWeightPercent:0}.");
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
