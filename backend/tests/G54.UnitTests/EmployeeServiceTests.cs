using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Employee;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

public sealed class EmployeeServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, EmployeeService Service) CreateTestEnvironment()
    {
        var context = CreateInMemoryDbContext();
        var service = new EmployeeService(context, NullLogger<EmployeeService>.Instance, new FakePermissionService(null, FakePermissionService.EmployeeReadAll), new PlainPasswordEncoder());
        return (context, service);
    }

    private static async Task<(Department Dept, JobPosition Position)> SeedDepartmentAndPositionAsync(AppDbContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D-TECH",
            Name = "Technology Department",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };

        var evaluationProfile = new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            Code = "EP-DEV",
            Name = "Software Engineer Profile",
            Version = 1,
            EffectiveFrom = DateOnly.FromDateTime(now.UtcDateTime),
            UpdatedAt = now,
            Status = EvaluationProfileStatus.Draft,
        };
        await context.EvaluationProfiles.AddAsync(evaluationProfile);

        var position = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "SALES",
            Title = "Senior Backend Engineer",
            Status = RecordStatus.Active,
            LastSyncAt = now,
            UpdatedAt = now,
        };
        evaluationProfile.JobPositionId = position.Id;

        await context.Departments.AddAsync(dept);
        await context.JobPositions.AddAsync(position);
        await context.SaveChangesAsync();

        return (dept, position);
    }

    [Fact]
    public async Task GetEmployeesAsync_WithFiltersAndPagination_ReturnsExpectedResults()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            var manager = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "MGR-001",
                FullName = "Nguyen Van Manager",
                Email = "manager@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };

            var dev1 = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "DEV-001",
                FullName = "Tran Van Dev",
                Email = "dev1@company.com",
                Phone = "0987654321",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                ManagerId = manager.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };

            var dev2 = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "DEV-002",
                FullName = "Le Thi Tester",
                Email = "tester@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Inactive,
                LastSyncAt = now,
                UpdatedAt = now,
            };

            await context.Employees.AddRangeAsync(manager, dev1, dev2);
            await context.SaveChangesAsync();

            // The keyword filter uses PostgreSQL ILike, so it is covered against a real database in EmployeeSearchHttpTests.

            // Filter by status Active
            var filterByStatus = new EmployeeFilterDto { Status = "ACTIVE" };
            var resultStatus = await service.GetEmployeesAsync(Guid.Empty, filterByStatus);
            Assert.Equal(2, resultStatus.TotalCount);

            // Filter by ManagerId
            var filterByManager = new EmployeeFilterDto { ManagerId = manager.Id };
            var resultManager = await service.GetEmployeesAsync(Guid.Empty, filterByManager);
            Assert.Single(resultManager.Items);
            Assert.Equal("DEV-001", resultManager.Items[0].EmployeeCode);
            Assert.Equal("MGR-001", resultManager.Items[0].ManagerCode);
        }
    }

    [Fact]
    public async Task GetEmployeeByIdAsync_WhenExists_ReturnsDetailedDtoWithManagerAndDirectReports()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            var manager = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "MGR-002",
                FullName = "Director Pham",
                Email = "director@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };

            var subordinate = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-002",
                FullName = "Hoang Subordinate",
                Email = "subordinate@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                ManagerId = manager.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };

            await context.Employees.AddRangeAsync(manager, subordinate);
            await context.SaveChangesAsync();

            var managerDetail = await service.GetEmployeeByIdAsync(Guid.Empty, manager.Id);
            Assert.NotNull(managerDetail);
            Assert.Equal("Director Pham", managerDetail.FullName);
            Assert.Equal(1, managerDetail.DirectReportsCount);

            var subDetail = await service.GetEmployeeByIdAsync(Guid.Empty, subordinate.Id);
            Assert.NotNull(subDetail);
            Assert.Equal("MGR-002", subDetail.ManagerCode);
            Assert.Equal("Director Pham", subDetail.ManagerName);
            Assert.Equal(0, subDetail.DirectReportsCount);
        }
    }

    [Fact]
    public async Task GetEmployeeByIdAsync_WhenNotFound_ReturnsNull()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var result = await service.GetEmployeeByIdAsync(Guid.Empty, Guid.NewGuid());
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task GetDepartmentOptionsAsync_ReturnsOnlyActiveDepartments()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var now = DateTimeOffset.UtcNow;
            var activeDept = new Department
            {
                Id = Guid.NewGuid(),
                Code = "D-ACT",
                Name = "Active Department",
                Status = RecordStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            var inactiveDept = new Department
            {
                Id = Guid.NewGuid(),
                Code = "D-INACT",
                Name = "Inactive Department",
                Status = RecordStatus.Inactive,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            await context.Departments.AddRangeAsync(activeDept, inactiveDept);
            await context.SaveChangesAsync();

            var options = await service.GetDepartmentOptionsAsync(Guid.Empty);
            Assert.Contains(options, o => o.Code == "D-ACT");
            Assert.DoesNotContain(options, o => o.Code == "D-INACT");
        }
    }

    [Fact]
    public async Task GetJobPositionOptionsAsync_ReturnsOnlyActivePositions()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var now = DateTimeOffset.UtcNow;
            var activePos = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "P-ACT",
                Title = "Active Position",
                Status = RecordStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            var inactivePos = new JobPosition
            {
                Id = Guid.NewGuid(),
                Code = "P-INACT",
                Title = "Inactive Position",
                Status = RecordStatus.Inactive,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            await context.JobPositions.AddRangeAsync(activePos, inactivePos);
            await context.SaveChangesAsync();

            var options = await service.GetJobPositionOptionsAsync(Guid.Empty);
            Assert.Contains(options, o => o.Code == "P-ACT");
            Assert.DoesNotContain(options, o => o.Code == "P-INACT");
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_ValidData_CreatesSuccessfully()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);

            var dto = new CreateEmployeeDto
            {
                EmployeeCode = "EMP-NEW-01",
                FullName = "Nguyen Thi New",
                Email = "newbie@company.com",
                Phone = "0912345678",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = "ACTIVE",
            };

            var created = await service.CreateEmployeeAsync(dto);

            Assert.NotNull(created);
            Assert.Equal("EMP-NEW-01", created.EmployeeCode);
            Assert.Equal("Nguyen Thi New", created.FullName);
            Assert.Equal("newbie@company.com", created.Email);
            Assert.Equal("0912345678", created.Phone);
            Assert.Equal(dept.Id, created.DepartmentId);
            Assert.Equal(position.Id, created.JobPositionId);

            var inDb = await context.Employees.SingleOrDefaultAsync(e => e.Id == created.Id);
            Assert.NotNull(inDb);
            Assert.Equal("EMP-NEW-01", inDb.EmployeeCode);
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_DuplicateCode_ThrowsConflictException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            var existing = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-DUP-01",
                FullName = "Existing Employee",
                Email = "existing1@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            await context.Employees.AddAsync(existing);
            await context.SaveChangesAsync();

            var dto = new CreateEmployeeDto
            {
                EmployeeCode = "EMP-DUP-01",
                FullName = "Duplicate Employee",
                Email = "different@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
            };

            await Assert.ThrowsAsync<ConflictException>(() => service.CreateEmployeeAsync(dto));
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_DuplicateEmail_ThrowsConflictException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            var existing = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-099",
                FullName = "Existing Employee",
                Email = "duplicate@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            await context.Employees.AddAsync(existing);
            await context.SaveChangesAsync();

            var dto = new CreateEmployeeDto
            {
                EmployeeCode = "EMP-100",
                FullName = "Another Employee",
                Email = "duplicate@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
            };

            await Assert.ThrowsAsync<ConflictException>(() => service.CreateEmployeeAsync(dto));
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_InvalidDepartmentOrPosition_ThrowsAppropriateException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);

            var invalidDeptDto = new CreateEmployeeDto
            {
                EmployeeCode = "EMP-INV-1",
                FullName = "Invalid Dept",
                Email = "inv1@company.com",
                DepartmentId = Guid.NewGuid(),
                JobPositionId = position.Id,
            };
            await Assert.ThrowsAsync<NotFoundException>(() => service.CreateEmployeeAsync(invalidDeptDto));

            var invalidPosDto = new CreateEmployeeDto
            {
                EmployeeCode = "EMP-INV-2",
                FullName = "Invalid Pos",
                Email = "inv2@company.com",
                DepartmentId = dept.Id,
                JobPositionId = Guid.NewGuid(),
            };
            await Assert.ThrowsAsync<NotFoundException>(() => service.CreateEmployeeAsync(invalidPosDto));
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_InvalidEmailOrPhoneFormat_ThrowsValidationException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);

            var badEmailDto = new CreateEmployeeDto
            {
                EmployeeCode = "EMP-BAD-1",
                FullName = "Bad Email",
                Email = "not-an-email",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
            };
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateEmployeeAsync(badEmailDto));

            var badPhoneDto = new CreateEmployeeDto
            {
                EmployeeCode = "EMP-BAD-2",
                FullName = "Bad Phone",
                Email = "good@company.com",
                Phone = "abc-xyz-invalid",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
            };
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateEmployeeAsync(badPhoneDto));
        }
    }

    [Fact]
    public async Task UpdateEmployeeAsync_ValidData_UpdatesSuccessfully()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-ORIG",
                FullName = "Original Name",
                Email = "orig@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            await context.Employees.AddAsync(employee);
            await context.SaveChangesAsync();

            var updateDto = new UpdateEmployeeDto
            {
                EmployeeCode = "EMP-MOD",
                FullName = "Modified Name",
                Email = "mod@company.com",
                Phone = "0909090909",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = "ON_LEAVE",
            };

            var updated = await service.UpdateEmployeeAsync(employee.Id, updateDto);

            Assert.Equal("EMP-MOD", updated.EmployeeCode);
            Assert.Equal("Modified Name", updated.FullName);
            Assert.Equal("mod@company.com", updated.Email);
            Assert.Equal("ON_LEAVE", updated.Status);
        }
    }

    [Fact]
    public async Task UpdateEmployeeAsync_SelfManager_ThrowsValidationException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-SELF",
                FullName = "Self Manager Employee",
                Email = "self@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            await context.Employees.AddAsync(employee);
            await context.SaveChangesAsync();

            var updateDto = new UpdateEmployeeDto
            {
                EmployeeCode = "EMP-SELF",
                FullName = "Self Manager Employee",
                Email = "self@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                ManagerId = employee.Id,
                Status = "ACTIVE",
            };

            await Assert.ThrowsAsync<ValidationException>(() => service.UpdateEmployeeAsync(employee.Id, updateDto));
        }
    }

    [Fact]
    public async Task UpdateEmployeeAsync_CircularManagerHierarchy_ThrowsValidationException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            // empA -> manager of empB
            var empA = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-A",
                FullName = "Employee A",
                Email = "a@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };

            var empB = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-B",
                FullName = "Employee B",
                Email = "b@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                ManagerId = empA.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };

            await context.Employees.AddRangeAsync(empA, empB);
            await context.SaveChangesAsync();

            // Attempt to make empB the manager of empA (creating cycle: empA -> empB -> empA)
            var updateDto = new UpdateEmployeeDto
            {
                EmployeeCode = "EMP-A",
                FullName = "Employee A",
                Email = "a@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                ManagerId = empB.Id,
                Status = "ACTIVE",
            };

            await Assert.ThrowsAsync<ValidationException>(() => service.UpdateEmployeeAsync(empA.Id, updateDto));
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_ValidStatus_UpdatesStatus()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var now = DateTimeOffset.UtcNow;

            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP-STATUS",
                FullName = "Status Employee",
                Email = "status@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = EmployeeStatus.Active,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            await context.Employees.AddAsync(employee);
            await context.SaveChangesAsync();

            await service.ChangeStatusAsync(employee.Id, new EmployeeStatusDto { Status = "TERMINATED" });

            var inDb = await context.Employees.FindAsync(employee.Id);
            Assert.NotNull(inDb);
            Assert.Equal(EmployeeStatus.Terminated, inDb.Status);
        }
    }

    private static Employee NewEmployeeEntity(Department dept, JobPosition position, string code, Guid? managerId = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = "Name " + code,
        Email = code.ToLowerInvariant() + "@company.com",
        DepartmentId = dept.Id,
        JobPositionId = position.Id,
        ManagerId = managerId,
        Status = EmployeeStatus.Active,
        LastSyncAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Account NewAccountFor(Employee employee) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employee.Id,
        Username = employee.Email,
        PasswordHash = "hash",
    };

    [Fact]
    public async Task DeleteEmployeeAsync_MovesEmployeeToTerminatedAndLocksAccount_WithoutRemovingTheRow()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var employee = NewEmployeeEntity(dept, position, "EMP-DEL");
            var account = NewAccountFor(employee);
            context.Employees.Add(employee);
            context.Accounts.Add(account);
            await context.SaveChangesAsync();

            await service.DeleteEmployeeAsync(employee.Id);

            var inDb = await context.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
            Assert.Equal(EmployeeStatus.Terminated, inDb.Status);
            Assert.True((await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id)).IsLocked);
        }
    }

    [Fact]
    public async Task DeleteEmployeeAsync_ForAManagerWithSubordinatesAndWork_StillTerminates()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var manager = NewEmployeeEntity(dept, position, "MGR-DEL");
            var subordinate = NewEmployeeEntity(dept, position, "SUB-DEL", manager.Id);
            context.Employees.AddRange(manager, subordinate);
            context.Works.Add(new Work
            {
                Id = Guid.NewGuid(),
                Code = "W-001",
                Title = "Important Task",
                AssigneeEmployeeId = manager.Id,
                CreatedBy = manager.Id,
                Status = WorkStatus.InProgress,
                Priority = WorkPriority.High,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();

            await service.DeleteEmployeeAsync(manager.Id);

            Assert.Equal(EmployeeStatus.Terminated, (await context.Employees.AsNoTracking().SingleAsync(e => e.Id == manager.Id)).Status);
            Assert.Equal(EmployeeStatus.Active, (await context.Employees.AsNoTracking().SingleAsync(e => e.Id == subordinate.Id)).Status);
        }
    }

    [Fact]
    public async Task DeleteEmployeeAsync_CalledTwice_IsIdempotent()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var employee = NewEmployeeEntity(dept, position, "EMP-TWICE");
            context.Employees.Add(employee);
            await context.SaveChangesAsync();

            await service.DeleteEmployeeAsync(employee.Id);
            await service.DeleteEmployeeAsync(employee.Id);

            Assert.Equal(EmployeeStatus.Terminated, (await context.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id)).Status);
        }
    }

    [Fact]
    public async Task DeleteEmployeeAsync_UnknownId_ThrowsNotFound()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteEmployeeAsync(Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_ToTerminated_LocksTheAccount_ButOtherStatusesDoNot()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var employee = NewEmployeeEntity(dept, position, "EMP-LOCK");
            var account = NewAccountFor(employee);
            context.Employees.Add(employee);
            context.Accounts.Add(account);
            await context.SaveChangesAsync();

            await service.ChangeStatusAsync(employee.Id, new EmployeeStatusDto { Status = "INACTIVE" });
            Assert.False((await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id)).IsLocked);

            await service.ChangeStatusAsync(employee.Id, new EmployeeStatusDto { Status = "TERMINATED" });
            Assert.True((await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id)).IsLocked);

            // Like the import, going back to ACTIVE never unlocks the account.
            await service.ChangeStatusAsync(employee.Id, new EmployeeStatusDto { Status = "ACTIVE" });
            Assert.True((await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id)).IsLocked);
        }
    }

    [Fact]
    public async Task UpdateEmployeeAsync_ToTerminated_LocksTheAccount()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var employee = NewEmployeeEntity(dept, position, "EMP-UPD-T");
            var account = NewAccountFor(employee);
            context.Employees.Add(employee);
            context.Accounts.Add(account);
            await context.SaveChangesAsync();

            await service.UpdateEmployeeAsync(employee.Id, new UpdateEmployeeDto
            {
                EmployeeCode = employee.EmployeeCode,
                FullName = employee.FullName,
                Email = employee.Email,
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = "TERMINATED",
            });

            Assert.True((await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == account.Id)).IsLocked);
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_CreatesAccountWithRoleAndOneTimePassword()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            var role = new Role { Id = Guid.NewGuid(), Code = "EMPLOYEE", Name = "Employee", Status = RecordStatus.Active, UpdatedAt = DateTimeOffset.UtcNow };
            context.Roles.Add(role);
            await context.SaveChangesAsync();

            var created = await service.CreateEmployeeAsync(new CreateEmployeeDto
            {
                EmployeeCode = "EMP-ACC",
                FullName = "Account Owner",
                Email = "Owner@Company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
            });

            Assert.False(string.IsNullOrEmpty(created.TemporaryPassword));
            Assert.Equal(16, created.TemporaryPassword!.Length);
            var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.EmployeeId == created.Id);
            Assert.Equal("owner@company.com", account.Username);
            Assert.False(account.IsLocked);
            Assert.True(new PlainPasswordEncoder().Verify(created.TemporaryPassword, account.PasswordHash));
            Assert.True(await context.UserRoles.AnyAsync(u => u.AccountId == account.Id && u.RoleId == role.Id));
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_AsTerminated_CreatesALockedAccount()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);

            var created = await service.CreateEmployeeAsync(new CreateEmployeeDto
            {
                EmployeeCode = "EMP-TERM-NEW",
                FullName = "Already Gone",
                Email = "gone@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
                Status = "TERMINATED",
            });

            Assert.True((await context.Accounts.AsNoTracking().SingleAsync(a => a.EmployeeId == created.Id)).IsLocked);
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_PositionWithoutRoleMapping_ThrowsValidation_AndCreatesNothing()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            position.Code = "NO_ROLE_FOR_THIS";
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateEmployeeAsync(new CreateEmployeeDto
            {
                EmployeeCode = "EMP-NOROLE",
                FullName = "No Role",
                Email = "norole@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
            }));

            Assert.False(await context.Employees.AnyAsync(e => e.EmployeeCode == "EMP-NOROLE"));
            Assert.Empty(context.Accounts);
        }
    }

    [Fact]
    public async Task CreateEmployeeAsync_UsernameAlreadyTaken_ThrowsConflict()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (dept, position) = await SeedDepartmentAndPositionAsync(context);
            context.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Username = "taken@company.com", PasswordHash = "hash" });
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<ConflictException>(() => service.CreateEmployeeAsync(new CreateEmployeeDto
            {
                EmployeeCode = "EMP-TAKEN",
                FullName = "Taken",
                Email = "taken@company.com",
                DepartmentId = dept.Id,
                JobPositionId = position.Id,
            }));
        }
    }
}
