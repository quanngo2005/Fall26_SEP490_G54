using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.SystemParameters;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class SystemParameterServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private async Task SeedParametersAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _db.SystemParameters.AddRange(
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.AppealWindowDays, NumericValue = 5, MinValue = 1, MaxValue = 30, IsLocked = false, Version = 0, CreatedAt = now, IsDeleted = false },
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.KpiLockTime, TextValue = "23:59:59", IsLocked = true, Version = 0, CreatedAt = now, IsDeleted = false },
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.HandoverThresholdVnd, NumericValue = 50000000, MinValue = 1, IsLocked = false, Version = 0, CreatedAt = now, IsDeleted = false },
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.ManualApprovalHours, NumericValue = 48, MinValue = 1, MaxValue = 168, IsLocked = false, Version = 0, CreatedAt = now, IsDeleted = false },
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.ImportMaxFileMb, NumericValue = 5, MinValue = 1, MaxValue = 10, IsLocked = false, Version = 0, CreatedAt = now, IsDeleted = false },
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.ImportFormats, TextValue = ".xlsx", IsLocked = true, Version = 0, CreatedAt = now, IsDeleted = false },
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.PreCloseCheckDays, NumericValue = 3, MinValue = 1, MaxValue = 30, IsLocked = false, Version = 0, CreatedAt = now, IsDeleted = false },
            new SystemParameter { Id = Guid.NewGuid(), Key = SystemParameterKeys.AppealRejectedHours, NumericValue = 48, IsLocked = true, Version = 0, CreatedAt = now, IsDeleted = false });
        await _db.SaveChangesAsync();
    }

    private async Task<(Account admin, Account nonAdmin)> SetupAccountsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var adminEmployee = new Employee { Id = Guid.NewGuid(), EmployeeCode = "ADM", FullName = "Admin", Email = "admin@test", DepartmentId = Guid.NewGuid(), JobPositionId = Guid.NewGuid(), Status = EmployeeStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var nonAdminEmployee = new Employee { Id = Guid.NewGuid(), EmployeeCode = "EMP", FullName = "Employee", Email = "emp@test", DepartmentId = Guid.NewGuid(), JobPositionId = Guid.NewGuid(), Status = EmployeeStatus.Active, LastSyncAt = now, UpdatedAt = now };

        var adminAccount = new Account { Id = Guid.NewGuid(), EmployeeId = adminEmployee.Id, Username = "admin", PasswordHash = "hash" };
        var nonAdminAccount = new Account { Id = Guid.NewGuid(), EmployeeId = nonAdminEmployee.Id, Username = "emp", PasswordHash = "hash" };

        var adminRole = new Role { Id = Guid.NewGuid(), Code = "SYSTEM_ADMIN", Name = "Admin" };
        var employeeRole = new Role { Id = Guid.NewGuid(), Code = "EMPLOYEE", Name = "Employee" };

        _db.Employees.Add(adminEmployee);
        _db.Employees.Add(nonAdminEmployee);
        _db.Accounts.Add(adminAccount);
        _db.Accounts.Add(nonAdminAccount);
        _db.Roles.Add(adminRole);
        _db.Roles.Add(employeeRole);

        _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = adminAccount.Id, RoleId = adminRole.Id });
        _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = nonAdminAccount.Id, RoleId = employeeRole.Id });

        await _db.SaveChangesAsync();
        return (adminAccount, nonAdminAccount);
    }

    [Fact]
    public async Task GetAll_ByAdmin_Returns8Parameters()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var result = await service.GetAllAsync(admin.Id, CancellationToken.None);

        Assert.Equal(8, result.Count);
        var appealDays = result.Single(p => p.Key == SystemParameterKeys.AppealWindowDays);
        Assert.Equal(5L, appealDays.Value);
        var importFormats = result.Single(p => p.Key == SystemParameterKeys.ImportFormats);
        Assert.Equal(".xlsx", importFormats.Value);
        Assert.True(importFormats.Locked);
    }

    [Fact]
    public async Task Save_WithValidChanges_UpdatesParametersAndRecordsHistory()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var request = new SaveSystemParametersDto
        {
            Changes = new List<SystemParameterChangeDto>
            {
                new() { Key = SystemParameterKeys.AppealWindowDays, Value = 7 },
                new() { Key = SystemParameterKeys.HandoverThresholdVnd, Value = 80000000 },
            }
        };

        var result = await service.SaveAsync(admin.Id, request, CancellationToken.None);

        Assert.Equal(8, result.Count);
        var appealDays = result.Single(p => p.Key == SystemParameterKeys.AppealWindowDays);
        Assert.Equal(7L, appealDays.Value);
        Assert.NotNull(appealDays.UpdatedBy);

        var changes = await _db.SystemParameterChanges.ToListAsync();
        Assert.Equal(2, changes.Count);
        var appealChange = changes.Single(c => c.Key == SystemParameterKeys.AppealWindowDays);
        Assert.Equal("5", appealChange.OldValue);
        Assert.Equal("7", appealChange.NewValue);
        Assert.Equal(admin.Id, appealChange.ChangedBy);

        var updated = await _db.SystemParameters.SingleAsync(p => p.Key == SystemParameterKeys.AppealWindowDays);
        Assert.Equal(1, updated.Version);
    }

    [Fact]
    public async Task Save_WithLockedParameter_ThrowsValidation()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var request = new SaveSystemParametersDto
        {
            Changes = new List<SystemParameterChangeDto>
            {
                new() { Key = SystemParameterKeys.KpiLockTime, Value = 0 }
            }
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SaveAsync(admin.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task Save_WithOutOfBoundsValue_ThrowsValidation()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var request = new SaveSystemParametersDto
        {
            Changes = new List<SystemParameterChangeDto>
            {
                new() { Key = SystemParameterKeys.AppealWindowDays, Value = 31 }
            }
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SaveAsync(admin.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task Save_WithUnchangedValue_DoesNotRecordHistory()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var origParam = await _db.SystemParameters.SingleAsync(p => p.Key == SystemParameterKeys.AppealWindowDays);
        var origVersion = origParam.Version;
        var origUpdatedAt = origParam.UpdatedAt;

        var request = new SaveSystemParametersDto
        {
            Changes = new List<SystemParameterChangeDto>
            {
                new() { Key = SystemParameterKeys.AppealWindowDays, Value = 5 }
            }
        };

        await service.SaveAsync(admin.Id, request, CancellationToken.None);

        var changes = await _db.SystemParameterChanges.ToListAsync();
        Assert.Empty(changes);

        var updated = await _db.SystemParameters.SingleAsync(p => p.Key == SystemParameterKeys.AppealWindowDays);
        Assert.Equal(origVersion, updated.Version);
        Assert.Equal(origUpdatedAt, updated.UpdatedAt);
    }

    [Fact]
    public async Task Save_WithEmptyChanges_ThrowsValidation()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var request = new SaveSystemParametersDto { Changes = new List<SystemParameterChangeDto>() };

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SaveAsync(admin.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task Save_WithUnknownKey_ThrowsValidation()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var request = new SaveSystemParametersDto
        {
            Changes = new List<SystemParameterChangeDto>
            {
                new() { Key = "unknownKey", Value = 10 }
            }
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SaveAsync(admin.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task Save_WithDuplicateKeys_ThrowsValidation()
    {
        await SeedParametersAsync();
        var (admin, _) = await SetupAccountsAsync();
        var uow = new UnitOfWork(_db);
        var service = new SystemParameterService(uow, TimeProvider.System);

        var request = new SaveSystemParametersDto
        {
            Changes = new List<SystemParameterChangeDto>
            {
                new() { Key = SystemParameterKeys.AppealWindowDays, Value = 7 },
                new() { Key = SystemParameterKeys.AppealWindowDays, Value = 8 }
            }
        };

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SaveAsync(admin.Id, request, CancellationToken.None));
    }
}
