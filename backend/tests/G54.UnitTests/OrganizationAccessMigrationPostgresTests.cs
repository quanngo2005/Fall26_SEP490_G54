using G54.BLL.Services;
using G54.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace G54.UnitTests;

/// <summary>
/// Runs the real migration chain on PostgreSQL and checks what <c>SeedOrganizationReadAccess</c> leaves in
/// <c>role</c>, <c>permission</c> and <c>role_permission</c>, then feeds those rows through the real
/// <see cref="PermissionService"/> to prove each role gets exactly the scope the organization screens need.
/// Skips when no Postgres is available (see <see cref="TestPostgres"/>); under <c>CI=true</c> a missing Postgres fails instead.
/// </summary>
/// <summary>One database and one EF service provider for the whole class (xunit builds a class instance per test).</summary>
public sealed class OrganizationAccessDatabaseFixture : IAsyncLifetime
{
    private string _database = null!;

    public string ConnectionString { get; private set; } = null!;

    public ServiceProvider? Provider { get; private set; }

    public async Task InitializeAsync()
    {
        if (TestPostgres.AdminConnectionString is not { } admin)
        {
            return;
        }

        _database = $"g54_orgaccess_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _database, Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(ConnectionString);
        Provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (Provider is null)
        {
            return;
        }

        await Provider.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestPostgres.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class OrganizationAccessMigrationPostgresTests(OrganizationAccessDatabaseFixture fixture)
    : IClassFixture<OrganizationAccessDatabaseFixture>, IAsyncLifetime
{
    private const string Migration = "20261027120000_SeedOrganizationReadAccess";
    private const string PreviousMigration = "20261027090000_SeedHandoverCreateAccess";

    private ServiceProvider _provider = null!;
    private string _connectionString = null!;

    /// <summary>Every test starts from the migration chain freshly applied, with the rows tests add removed again.</summary>
    public async Task InitializeAsync()
    {
        if (fixture.Provider is null)
        {
            return;
        }

        _provider = fixture.Provider;
        _connectionString = fixture.ConnectionString;
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        // Back to a clean slate: only what the migration itself created may remain.
        await ExecuteAsync("""
            DELETE FROM user_role;
            DELETE FROM account;
            DELETE FROM employee;
            DELETE FROM job_position;
            DELETE FROM department;
            DELETE FROM role WHERE code NOT IN ('SYSTEM_ADMIN','BRANCH_MANAGER','DEPT_MANAGER');
            """);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await db.GetService<IMigrator>().MigrateAsync(Migration);
    }

    public Task DisposeAsync() => Task.CompletedTask;
    [PostgresFact]
    public async Task MigrationCreatesTheThreeRolesAndThePermission()
    {
        var roles = await QueryAsync("SELECT code FROM role WHERE code IN ('SYSTEM_ADMIN','BRANCH_MANAGER','DEPT_MANAGER') ORDER BY code");
        var permission = await QueryAsync("SELECT resource || ':' || action FROM permission WHERE code = 'ORGANIZATION_READ'");

        Assert.Equal(["BRANCH_MANAGER", "DEPT_MANAGER", "SYSTEM_ADMIN"], roles);
        Assert.Equal(["organization:read"], permission);
    }

    [PostgresFact]
    public async Task MigrationGrantsExactlyTheAllowedRolesWithTheirScope()
    {
        var grants = await QueryAsync("""
            SELECT r.code || '=' || rp.scope
            FROM role_permission rp
            JOIN role r ON r.id = rp.role_id
            JOIN permission p ON p.id = rp.permission_id
            WHERE p.code = 'ORGANIZATION_READ'
            ORDER BY r.code
            """);

        // ADMIN and MANAGER are created by the development seed, not by migrations, so an empty database has no row
        // for them; the next test covers the case where they exist.
        Assert.Equal(["BRANCH_MANAGER=Branch", "DEPT_MANAGER=Dept", "SYSTEM_ADMIN=All"], grants);
        Assert.DoesNotContain(grants, g => g.StartsWith("EMPLOYEE", StringComparison.Ordinal) || g.StartsWith("VERIFIER", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task RunningTheSeedAgainAddsNoDuplicatesAndKeepsAdjustedScopes()
    {
        await ExecuteAsync("UPDATE role_permission SET scope = 'All' WHERE role_id = (SELECT id FROM role WHERE code = 'DEPT_MANAGER')");

        await RunUpAgainAsync();

        Assert.Equal(["3"], await QueryAsync("SELECT count(*)::text FROM role WHERE code IN ('SYSTEM_ADMIN','BRANCH_MANAGER','DEPT_MANAGER')"));
        Assert.Equal(["1"], await QueryAsync("SELECT count(*)::text FROM permission WHERE code = 'ORGANIZATION_READ'"));
        Assert.Equal(["3"], await QueryAsync("SELECT count(*)::text FROM role_permission rp JOIN permission p ON p.id = rp.permission_id WHERE p.code = 'ORGANIZATION_READ'"));
        // An administrator's later change is not overwritten when the statements run again.
        Assert.Equal(["All"], await QueryAsync("SELECT scope FROM role_permission WHERE role_id = (SELECT id FROM role WHERE code = 'DEPT_MANAGER')"));
    }

    [PostgresFact]
    public async Task ItGrantsTheAdminAndManagerRolesWhenTheyAlreadyExist()
    {
        await ExecuteAsync("""
            INSERT INTO role (code, name) VALUES ('ADMIN', 'Administrator'), ('MANAGER', 'Manager'), ('EMPLOYEE', 'Employee')
            ON CONFLICT (code) DO NOTHING
            """);

        await ReapplyAsync();

        var grants = await QueryAsync("""
            SELECT r.code || '=' || rp.scope FROM role_permission rp
            JOIN role r ON r.id = rp.role_id JOIN permission p ON p.id = rp.permission_id
            WHERE p.code = 'ORGANIZATION_READ' ORDER BY r.code
            """);
        Assert.Contains("ADMIN=All", grants);
        Assert.Contains("MANAGER=Dept", grants);
        Assert.DoesNotContain(grants, g => g.StartsWith("EMPLOYEE", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task EachRoleResolvesToItsScopeThroughThePermissionService()
    {
        await ExecuteAsync("""
            INSERT INTO role (code, name) VALUES ('ADMIN', 'Administrator'), ('MANAGER', 'Manager'), ('EMPLOYEE', 'Employee'), ('MANAGER', 'Manager')
            ON CONFLICT (code) DO NOTHING
            """);
        await ReapplyAsync();
        var accounts = new Dictionary<string, Guid>();
        foreach (var role in new[] { "SYSTEM_ADMIN", "ADMIN", "BRANCH_MANAGER", "DEPT_MANAGER", "MANAGER", "EMPLOYEE" })
        {
            accounts[role] = await CreateAccountWithRoleAsync(role);
        }

        await using var scope = _provider.CreateAsyncScope();
        var service = new PermissionService(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        // "Dept" is the lowest scope the organization endpoints require.
        var canOpen = new Dictionary<string, bool>();
        foreach (var (role, account) in accounts)
        {
            canOpen[role] = await service.HasPermissionAsync(account, "organization", "read", "Dept", CancellationToken.None);
        }

        Assert.True(canOpen["SYSTEM_ADMIN"]);
        Assert.True(canOpen["ADMIN"]);
        Assert.True(canOpen["BRANCH_MANAGER"]);
        Assert.True(canOpen["DEPT_MANAGER"]);
        Assert.True(canOpen["MANAGER"]);
        Assert.False(canOpen["EMPLOYEE"]);

        var branch = await service.GetEffectivePermissionsAsync(accounts["BRANCH_MANAGER"], CancellationToken.None);
        Assert.Equal("Branch", branch.Single(p => p.Resource == "organization" && p.Action == "read").Scope);
        var dept = await service.GetEffectivePermissionsAsync(accounts["DEPT_MANAGER"], CancellationToken.None);
        Assert.Equal("Dept", dept.Single(p => p.Resource == "organization" && p.Action == "read").Scope);
        var system = await service.GetEffectivePermissionsAsync(accounts["SYSTEM_ADMIN"], CancellationToken.None);
        Assert.Equal("All", system.Single(p => p.Resource == "organization" && p.Action == "read").Scope);
    }

    [PostgresFact]
    public async Task RollingBackRemovesThePermissionAndOnlyTheUnusedRoles()
    {
        var accountId = await CreateAccountWithRoleAsync("DEPT_MANAGER");

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().GetService<IMigrator>()
                .MigrateAsync(PreviousMigration);
        }

        Assert.Empty(await QueryAsync("SELECT code FROM permission WHERE code = 'ORGANIZATION_READ'"));
        Assert.Empty(await QueryAsync("SELECT code FROM role WHERE code IN ('SYSTEM_ADMIN','BRANCH_MANAGER')"));
        // A role somebody already holds survives, so the rollback cannot silently strip an assignment.
        Assert.Equal(["DEPT_MANAGER"], await QueryAsync("SELECT code FROM role WHERE code = 'DEPT_MANAGER'"));
        Assert.Equal(["1"], await QueryAsync($"SELECT count(*)::text FROM user_role WHERE account_id = '{accountId}'"));

        await using var again = _provider.CreateAsyncScope();
        await again.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        Assert.Equal(["1"], await QueryAsync("SELECT count(*)::text FROM permission WHERE code = 'ORGANIZATION_READ'"));
    }

    [Fact]
    public void TheMigrationFollowsTheHandoverMigration()
    {
        using var scope = fixture.Provider?.CreateScope() ?? new ServiceCollection().AddDataAccess("Host=unused").BuildServiceProvider().CreateScope();
        var migrations = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetMigrations().ToList();

        var index = migrations.IndexOf(Migration);
        Assert.True(index > 0, "the organization access migration must be part of the chain");
        Assert.Equal(PreviousMigration, migrations[index - 1]);
    }

    /// <summary>
    /// Executes the migration's <c>Up</c> statements a second time on top of the current data (no <c>Down</c> in between),
    /// the way a re-applied seed or a database that already holds some of these rows would see them.
    /// </summary>
    private async Task RunUpAgainAsync()
    {
        var up = typeof(G54.DAL.Migrations.SeedOrganizationReadAccess).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        up.Invoke(new G54.DAL.Migrations.SeedOrganizationReadAccess(), [builder]);
        foreach (var operation in builder.Operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
        {
            await ExecuteAsync(operation.Sql);
        }
    }

    /// <summary>Rolls back one migration and applies it again, the way a redeploy after a rollback would.</summary>
    private async Task ReapplyAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await db.GetService<IMigrator>().MigrateAsync(Migration);
    }

    private async Task<Guid> CreateAccountWithRoleAsync(string roleCode)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var department = Guid.NewGuid();
        var position = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var account = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO department (id, code, name, level_type) VALUES ('{department}', 'D{suffix}', 'D', 'DEPARTMENT');
            INSERT INTO job_position (id, code, title) VALUES ('{position}', 'P{suffix}', 'P');
            INSERT INTO employee (id, employee_code, full_name, email, department_id, job_position_id)
              VALUES ('{employee}', 'E{suffix}', 'E', 'e{suffix}@test.local', '{department}', '{position}');
            INSERT INTO account (id, employee_id, username, password_hash) VALUES ('{account}', '{employee}', 'u{suffix}', 'x');
            INSERT INTO user_role (account_id, role_id) SELECT '{account}', id FROM role WHERE code = '{roleCode}';
            """);
        return account;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
