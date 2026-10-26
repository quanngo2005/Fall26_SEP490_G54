using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Employee;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

/// <summary>Employee lookup by code and "me" (URLs use the code, never the GUID) and the 10-digit phone rule.</summary>
public sealed class EmployeeLookupTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private Department _department = null!;
    private JobPosition _position = null!;
    private bool _seeded;

    public void Dispose() => _db.Dispose();

    private EmployeeService Service => new(_db, NullLogger<EmployeeService>.Instance, new FakePermissionService(null, FakePermissionService.EmployeeReadAll));

    private async Task SeedReferenceDataAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _department = new Department { Id = Guid.NewGuid(), Code = "CN-HN-KD", Name = "Phòng Kinh doanh", LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        _position = new JobPosition { Id = Guid.NewGuid(), Code = "CV", Title = "Chuyên viên", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        _db.AddRange(_department, _position);
        await _db.SaveChangesAsync();
        _seeded = true;
    }

    private async Task<(Employee Employee, Account Account)> AddEmployeeAsync(
        string code,
        string? phone = "0901000004",
        EmployeeStatus status = EmployeeStatus.Active,
        bool locked = false,
        Guid? managerId = null)
    {
        if (!_seeded)
        {
            await SeedReferenceDataAsync();
        }

        var now = DateTimeOffset.UtcNow;
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Employee {code}",
            Email = $"{code.ToLowerInvariant()}@bank.test",
            Phone = phone,
            DepartmentId = _department.Id,
            JobPositionId = _position.Id,
            ManagerId = managerId,
            Status = status,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "hash", IsLocked = locked, LastSyncAt = now, UpdatedAt = now };
        _db.AddRange(employee, account);
        await _db.SaveChangesAsync();
        return (employee, account);
    }

    // ---- by code -----------------------------------------------------------------------------------

    [Fact]
    public async Task ByCode_ReturnsTheEmployeeWithDepartmentAndManagerCodes()
    {
        var (manager, _) = await AddEmployeeAsync("NV001");
        var (employee, _) = await AddEmployeeAsync("NV003", managerId: manager.Id);

        var result = await Service.GetEmployeeByCodeAsync(Guid.Empty, "NV003");

        Assert.NotNull(result);
        Assert.Equal(employee.Id, result.Id);
        Assert.Equal("NV003", result.EmployeeCode);
        Assert.Equal("CN-HN-KD", result.DepartmentCode);
        Assert.Equal("NV001", result.ManagerCode);
        Assert.Equal("Employee NV001", result.ManagerName);
    }

    [Fact]
    public async Task ByCode_TrimsTheCode()
    {
        await AddEmployeeAsync("NV003");

        Assert.NotNull(await Service.GetEmployeeByCodeAsync(Guid.Empty, "  NV003 "));
    }

    [Theory]
    [InlineData("NV999")]
    [InlineData("nv003")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ByCode_OfAnUnknownOrBlankCode_IsNull(string code)
    {
        await AddEmployeeAsync("NV003");

        Assert.Null(await Service.GetEmployeeByCodeAsync(Guid.Empty, code));
    }

    [Fact]
    public async Task ByCode_DoesNotMatchAPartOfACode()
    {
        await AddEmployeeAsync("NV003");

        Assert.Null(await Service.GetEmployeeByCodeAsync(Guid.Empty, "NV00"));
    }

    [Fact]
    public async Task ByCode_AndById_ReturnTheSameShape()
    {
        var (employee, _) = await AddEmployeeAsync("NV003");

        var byCode = await Service.GetEmployeeByCodeAsync(Guid.Empty, "NV003");
        var byId = await Service.GetEmployeeByIdAsync(Guid.Empty, employee.Id);

        Assert.Equal(byId, byCode);
    }

    // ---- me ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Current_ReturnsTheEmployeeBehindTheAccount()
    {
        await AddEmployeeAsync("NV001");
        var (employee, account) = await AddEmployeeAsync("NV004");

        var result = await Service.GetCurrentEmployeeAsync(account.Id);

        Assert.Equal(employee.Id, result.Id);
        Assert.Equal("NV004", result.EmployeeCode);
    }

    [Fact]
    public async Task Current_OfAnUnknownAccount_IsNotFound()
    {
        await AddEmployeeAsync("NV004");

        await Assert.ThrowsAsync<NotFoundException>(() => Service.GetCurrentEmployeeAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Current_OfALockedAccount_IsForbidden()
    {
        var (_, account) = await AddEmployeeAsync("NV004", locked: true);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.GetCurrentEmployeeAsync(account.Id));
    }

    [Fact]
    public async Task Current_OfAnInactiveEmployee_IsForbidden()
    {
        var (_, account) = await AddEmployeeAsync("NV004", status: EmployeeStatus.Inactive);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.GetCurrentEmployeeAsync(account.Id));
    }

    // ---- phone rule on the admin API ----------------------------------------------------------------

    private CreateEmployeeDto CreateDto(string? phone) => new()
    {
        EmployeeCode = "NV100",
        FullName = "New Hire",
        Email = "new.hire@bank.test",
        Phone = phone,
        DepartmentId = _department.Id,
        JobPositionId = _position.Id,
        Status = "ACTIVE",
    };

    [Theory]
    [InlineData("0912345678")]
    [InlineData("  0912345678 ")]
    [InlineData(null)]
    [InlineData("")]
    public async Task Create_WithAValidOrBlankPhone_Succeeds(string? phone)
    {
        await SeedReferenceDataAsync();

        var created = await Service.CreateEmployeeAsync(CreateDto(phone));

        Assert.Equal(string.IsNullOrWhiteSpace(phone) ? null : "0912345678", created.Phone);
    }

    [Theory]
    [InlineData("+84912345678")]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("1912345678")]
    [InlineData("0912 345 678")]
    [InlineData("abc-xyz-invalid")]
    public async Task Create_WithAnInvalidPhone_IsRejected(string phone)
    {
        await SeedReferenceDataAsync();

        var thrown = await Assert.ThrowsAsync<ValidationException>(() => Service.CreateEmployeeAsync(CreateDto(phone)));

        Assert.Contains("10 chữ số", thrown.Message, StringComparison.Ordinal);
    }

    private static UpdateEmployeeDto UpdateDto(Employee employee, string? phone, string fullName = "Renamed") => new()
    {
        EmployeeCode = employee.EmployeeCode,
        FullName = fullName,
        Email = employee.Email,
        Phone = phone,
        DepartmentId = employee.DepartmentId,
        JobPositionId = employee.JobPositionId,
        Status = "ACTIVE",
    };

    [Fact]
    public async Task Update_WithANewValidPhone_Succeeds()
    {
        var (employee, _) = await AddEmployeeAsync("NV004");

        var updated = await Service.UpdateEmployeeAsync(employee.Id, UpdateDto(employee, "0987654321"));

        Assert.Equal("0987654321", updated.Phone);
    }

    [Theory]
    [InlineData("+84987654321")]
    [InlineData("987654321")]
    public async Task Update_WithANewInvalidPhone_IsRejectedAndNothingChanges(string phone)
    {
        var (employee, _) = await AddEmployeeAsync("NV004");

        await Assert.ThrowsAsync<ValidationException>(() => Service.UpdateEmployeeAsync(employee.Id, UpdateDto(employee, phone)));

        var stored = await _db.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
        Assert.Equal("0901000004", stored.Phone);
        Assert.Equal("Employee NV004", stored.FullName);
    }

    [Fact]
    public async Task Update_KeepingAStoredPhoneThatPredatesTheRule_StillSavesOtherChanges()
    {
        var (employee, _) = await AddEmployeeAsync("NV004", phone: "+84 901 000 004");

        var updated = await Service.UpdateEmployeeAsync(employee.Id, UpdateDto(employee, "+84 901 000 004"));

        Assert.Equal("Renamed", updated.FullName);
        Assert.Equal("+84 901 000 004", updated.Phone);
    }

    [Fact]
    public async Task Update_ChangingAnOldFormatPhoneToAnotherInvalidOne_IsRejected()
    {
        var (employee, _) = await AddEmployeeAsync("NV004", phone: "+84 901 000 004");

        await Assert.ThrowsAsync<ValidationException>(() => Service.UpdateEmployeeAsync(employee.Id, UpdateDto(employee, "+84 901 000 005")));
    }

    [Fact]
    public async Task Update_ClearingThePhone_Succeeds()
    {
        var (employee, _) = await AddEmployeeAsync("NV004");

        var updated = await Service.UpdateEmployeeAsync(employee.Id, UpdateDto(employee, "  "));

        Assert.Null(updated.Phone);
    }

    // ---- controller --------------------------------------------------------------------------------

    private EmployeeController ControllerFor(Guid accountId) => new(Service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", accountId.ToString())], "TestAuth")),
            },
        },
    };

    [Fact]
    public async Task Controller_GetEmployeeByCode_ReturnsTheEnvelopeOr404()
    {
        await AddEmployeeAsync("NV003");
        var controller = ControllerFor(Guid.NewGuid());

        var found = await controller.GetEmployeeByCode("NV003", CancellationToken.None);
        var missing = await controller.GetEmployeeByCode("NV999", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(found.Result);
        Assert.Equal("NV003", Assert.IsType<ApiResponse<EmployeeDetailDto>>(ok.Value).Data!.EmployeeCode);
        var notFound = Assert.IsType<NotFoundObjectResult>(missing.Result);
        Assert.Equal(404, Assert.IsType<ApiResponse>(notFound.Value).Status);
    }

    [Fact]
    public async Task Controller_GetCurrentEmployee_UsesTheSignedInAccount()
    {
        await AddEmployeeAsync("NV001");
        var (_, account) = await AddEmployeeAsync("NV004");

        var result = await ControllerFor(account.Id).GetCurrentEmployee(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("NV004", Assert.IsType<ApiResponse<EmployeeDetailDto>>(ok.Value).Data!.EmployeeCode);
    }

    [Fact]
    public async Task Controller_GetCurrentEmployee_LetsServiceErrorsReachTheGlobalHandler()
    {
        await AddEmployeeAsync("NV001");

        await Assert.ThrowsAsync<NotFoundException>(() => ControllerFor(Guid.NewGuid()).GetCurrentEmployee(CancellationToken.None));
    }
}
