using G54.BLL.Common.Models;
using G54.BLL.Dtos.Employee;

namespace G54.BLL.Services;

public interface IEmployeeService
{
    /// <summary>Employees visible to <paramref name="callerAccountId"/> under their employee:read scope.</summary>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The caller holds no employee:read scope.</exception>
    Task<PagedResult<EmployeeDetailDto>> GetEmployeesAsync(Guid callerAccountId, EmployeeFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>Null when the employee is unknown or outside the caller's employee:read scope.</summary>
    Task<EmployeeDetailDto?> GetEmployeeByIdAsync(Guid callerAccountId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks an employee up by their (unique) employee code, which is what URLs use instead of the GUID.</summary>
    Task<EmployeeDetailDto?> GetEmployeeByCodeAsync(Guid callerAccountId, string code, CancellationToken cancellationToken = default);

    /// <summary>The employee behind <paramref name="accountId"/>.</summary>
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">No such account.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The account is locked or the employee is not active.</exception>
    Task<EmployeeDetailDto> GetCurrentEmployeeAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<List<DepartmentOptionDto>> GetDepartmentOptionsAsync(CancellationToken cancellationToken = default);
    Task<List<JobPositionOptionDto>> GetJobPositionOptionsAsync(CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto> CreateEmployeeAsync(CreateEmployeeDto dto, CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto> UpdateEmployeeAsync(Guid id, UpdateEmployeeDto dto, CancellationToken cancellationToken = default);
    Task ChangeStatusAsync(Guid id, EmployeeStatusDto dto, CancellationToken cancellationToken = default);
    Task DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default);
}
