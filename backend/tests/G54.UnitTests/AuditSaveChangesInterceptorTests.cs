using G54.DAL;
using G54.DAL.Auditing;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class AuditSaveChangesInterceptorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly AppDbContext _db;

    public AuditSaveChangesInterceptorTests()
    {
        var interceptor = new AuditSaveChangesInterceptor(
            new FixedAuditContextProvider(new AuditContext(ActorId, "tester", "127.0.0.1", "xunit", "POST", "/test", "trace-1")),
            new FixedTime(Now),
            NullLogger<AuditSaveChangesInterceptor>.Instance);

        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_WritesAuditLogAndStampsCreatedBy()
    {
        var department = NewDepartment();
        _db.Departments.Add(department);

        await _db.SaveChangesAsync();

        var log = await _db.AuditLogs.SingleAsync(a => a.EntityName == nameof(Department) && a.EntityId == department.Id.ToString());
        Assert.Equal("Create", log.Action);
        Assert.Equal(ActorId, log.ActorId);
        Assert.Equal("tester", log.ActorName);
        Assert.Equal("trace-1", log.TraceId);
        Assert.Contains(department.Code, log.NewValues);
        Assert.Null(log.OldValues);
    }

    [Fact]
    public async Task Update_WritesOnlyChangedColumnsAndStampsUpdatedBy()
    {
        var sidebar = NewSidebarConfiguration();
        _db.Set<SidebarConfiguration>().Add(sidebar);
        await _db.SaveChangesAsync();

        sidebar.Version = 2;
        await _db.SaveChangesAsync();

        var log = await _db.AuditLogs
            .Where(a => a.EntityName == nameof(SidebarConfiguration) && a.Action == "Update")
            .SingleAsync();
        Assert.NotNull(log.ChangedColumns);
        Assert.Contains("Version", log.ChangedColumns);
        Assert.DoesNotContain("Role", log.ChangedColumns);
        Assert.Equal(ActorId, sidebar.UpdatedBy);
    }

    [Fact]
    public async Task SoftDelete_IsLoggedAsSoftDeleteNotUpdate()
    {
        var sidebar = NewSidebarConfiguration();
        _db.Set<SidebarConfiguration>().Add(sidebar);
        await _db.SaveChangesAsync();

        sidebar.IsDeleted = true;
        await _db.SaveChangesAsync();

        var log = await _db.AuditLogs
            .SingleAsync(a => a.EntityName == nameof(SidebarConfiguration) && a.Action == "SoftDelete");
        Assert.Equal(sidebar.Id.ToString(), log.EntityId);
    }

    [Fact]
    public async Task AuditIgnoredEntity_IsNotLogged()
    {
        _db.AuthAuditLogs.Add(new AuthAuditLog
        {
            Id = Guid.NewGuid(),
            Action = "LOGIN",
            OccurredAt = Now,
        });

        await _db.SaveChangesAsync();

        Assert.False(await _db.AuditLogs.AnyAsync(a => a.EntityName == nameof(AuthAuditLog)));
    }

    [Fact]
    public async Task Create_MasksPasswordHash()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "E1",
            FullName = "Employee One",
            Email = "e1@bank.test",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = "e1",
            PasswordHash = "super-secret-hash",
        });

        await _db.SaveChangesAsync();

        var log = await _db.AuditLogs.SingleAsync(a => a.EntityName == nameof(Account));
        Assert.DoesNotContain("super-secret-hash", log.NewValues);
        Assert.Contains("***", log.NewValues);
    }

    [Fact]
    public async Task WritingAuditLogsDoesNotRecurse()
    {
        var department = NewDepartment();
        _db.Departments.Add(department);

        await _db.SaveChangesAsync();

        // One audit row for the department create; the AuditLog row itself must not generate another.
        Assert.Equal(1, await _db.AuditLogs.CountAsync());
    }

    private static Department NewDepartment() => new()
    {
        Id = Guid.NewGuid(),
        Code = $"DEP-{Guid.NewGuid():N}"[..10],
        Name = "Test Department",
        LevelType = DepartmentLevelType.Department,
        Status = RecordStatus.Active,
    };

    private static SidebarConfiguration NewSidebarConfiguration() => new()
    {
        Id = Guid.NewGuid(),
        Role = "ADMIN",
        ItemIds = ["dashboard"],
        Version = 1,
    };

    private sealed class FixedAuditContextProvider(AuditContext context) : IAuditContextProvider
    {
        public AuditContext GetContext() => context;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
