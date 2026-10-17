using G54.BLL.Common.Models;
using G54.BLL.Dtos.Employee;

namespace G54.BLL.Services;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeDetailDto>> GetEmployeesAsync(EmployeeFilterDto filter, CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto?> GetEmployeeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<DepartmentOptionDto>> GetDepartmentOptionsAsync(CancellationToken cancellationToken = default);
    Task<List<JobPositionOptionDto>> GetJobPositionOptionsAsync(CancellationToken cancellationToken = default);
    Task<List<ManagerOptionDto>> GetManagerOptionsAsync(Guid? excludeEmployeeId = null, CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto> CreateEmployeeAsync(CreateEmployeeDto dto, CancellationToken cancellationToken = default);
    Task<EmployeeDetailDto> UpdateEmployeeAsync(Guid id, UpdateEmployeeDto dto, CancellationToken cancellationToken = default);
    Task ChangeStatusAsync(Guid id, EmployeeStatusDto dto, CancellationToken cancellationToken = default);
    Task DeleteEmployeeAsync(Guid id, CancellationToken cancellationToken = default);
}
