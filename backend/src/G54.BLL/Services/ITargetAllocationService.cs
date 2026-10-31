using G54.BLL.Dtos.TargetAllocation;

namespace G54.BLL.Services;

public interface ITargetAllocationService
{
    Task<TargetImportResultDto> ImportYearTargetsAsync(Guid branchId, int year, Stream excel, Guid userId, CancellationToken ct = default);

    Task<List<TargetNodeDto>> AllocateDepartmentsAsync(Guid branchYearId, AllocateDepartmentsDto dto, Guid userId, CancellationToken ct = default);

    Task<List<TargetNodeDto>> AllocateQuartersAsync(Guid deptYearId, AllocateQuartersDto dto, Guid userId, CancellationToken ct = default);

    Task<TargetNodeDto> ConfirmAsync(Guid deptYearId, Guid userId, CancellationToken ct = default);

    Task<List<TargetNodeDto>> GetTreeAsync(Guid branchId, int year, int? compareYear, Guid userId, CancellationToken ct = default);
}
