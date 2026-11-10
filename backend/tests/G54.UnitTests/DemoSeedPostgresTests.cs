using G54.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace G54.UnitTests;

/// <summary>One database and one EF service provider for the whole class (xunit builds a class instance per test).</summary>
public sealed class DemoSeedDatabaseFixture : IAsyncLifetime
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

        _database = $"g54_demoseed_{Guid.NewGuid():N}";
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

        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        // Same order as the DbMigrator in Development: migrations first, then the development seed with demo accounts.
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
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

/// <summary>
/// Runs the real migrations and the real development seed (<c>seed_dev_data.sql</c> + demo accounts) on PostgreSQL and
/// checks what the demo accounts end up with: exactly one role each, and every role usable on its own, so a role can be
/// tried by signing in as one account. Skips when no Postgres is available (see <see cref="TestPostgres"/>).
/// </summary>
public sealed class DemoSeedPostgresTests(DemoSeedDatabaseFixture fixture) : IClassFixture<DemoSeedDatabaseFixture>
{
    private static readonly string[] ExpectedAccountRoles =
    [
        "NV001=BRANCH_MANAGER",
        "NV002=DEPT_MANAGER",
        "NV003=DEPT_MANAGER",
        "NV004=EMPLOYEE",
        "NV005=EMPLOYEE",
        "NV006=EMPLOYEE",
        "NV007=EMPLOYEE",
        "NV008=EMPLOYEE",
        "NV009=EMPLOYEE",
        "NV010=EMPLOYEE",
        "NV011=DEPT_MANAGER",
        "NV012=EMPLOYEE",
        "NV013=SYSTEM_ADMIN",
        "NV014=VERIFIER",
        "NV015=DEPT_MANAGER",
        "NV016=EMPLOYEE",
        "NV017=EMPLOYEE",
        "NV018=DEPT_MANAGER",
        "NV019=EMPLOYEE",
        "NV020=DEPT_MANAGER",
        "NV021=EMPLOYEE",
    ];

    [PostgresFact]
    public async Task EveryDemoAccountHasExactlyOneRole()
    {
        var perAccount = await QueryAsync("""
            SELECT e.employee_code || '=' || count(ur.id)
            FROM account a
            JOIN employee e ON e.id = a.employee_id
            LEFT JOIN user_role ur ON ur.account_id = a.id
            WHERE e.employee_code LIKE 'NV%'
            GROUP BY e.employee_code
            ORDER BY e.employee_code
            """);

        Assert.Equal(21, perAccount.Count);
        Assert.All(perAccount, row => Assert.EndsWith("=1", row, StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task EachDemoAccountHoldsTheRoleItIsMeantToTest()
    {
        var roles = await QueryAsync("""
            SELECT e.employee_code || '=' || r.code
            FROM user_role ur
            JOIN account a ON a.id = ur.account_id
            JOIN employee e ON e.id = a.employee_id
            JOIN role r ON r.id = ur.role_id
            WHERE e.employee_code LIKE 'NV%'
            ORDER BY e.employee_code
            """);

        Assert.Equal(ExpectedAccountRoles, roles);
    }

    [PostgresFact]
    public async Task OrganizationRolesAreUsableOnTheirOwnWithTheSameWorkPermissionsAsTheirSourceRole()
    {
        // MANAGER's permissions, minus nothing: DEPT_MANAGER must be able to do everything MANAGER can.
        var deptManager = (await GrantsAsync("DEPT_MANAGER"))
            .Where(grant => !grant.StartsWith("TRANSACTION_IMPORT_MANAGE=", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(await GrantsAsync("MANAGER"), deptManager);
        Assert.Contains("TRANSACTION_IMPORT_MANAGE=All", await GrantsAsync("DEPT_MANAGER"));
    }

    [PostgresFact]
    public async Task BranchManagerGetsTheManagerPermissionsWithDepartmentScopesWidenedToBranch()
    {
        var manager = await GrantsAsync("MANAGER");
        var branch = await GrantsAsync("BRANCH_MANAGER");

        var widened = manager
            .Select(grant => grant.Replace("=Department", "=Branch", StringComparison.Ordinal).Replace("=Dept", "=Branch", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        // Approving evaluation profiles, and owning objectives and KPI requests / approvals, are the Branch Manager's own
        // permissions, not copies of the manager's. Reading objectives is the manager's own grant, widened like the others.
        string[] ownGrants = ["EVALUATION_PROFILE_APPROVE=", "OBJECTIVE_MANAGE=", "KPI_REQUEST=", "KPI_APPROVE=", "TRANSACTION_IMPORT_MANAGE="];
        Assert.Equal(widened, branch.Where(grant => !ownGrants.Any(own => grant.StartsWith(own, StringComparison.Ordinal))));
        Assert.Contains("EVALUATION_PROFILE_APPROVE=All", branch);
        Assert.Contains("OBJECTIVE_MANAGE=All", branch);
        Assert.Contains("KPI_REQUEST=All", branch);
        Assert.Contains("KPI_APPROVE=All", branch);
        Assert.Contains("TRANSACTION_IMPORT_MANAGE=All", branch);
        Assert.Contains("OBJECTIVE_READ=Branch", branch);
        Assert.Contains("WORK_READ=Branch", branch);
        Assert.Contains("KPI_READ=All", branch); // wider scopes are kept
        Assert.Contains("HANDOVER_CREATE=Own", branch); // narrower scopes are kept
        Assert.DoesNotContain(branch, grant => grant.EndsWith("=Department", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task SystemAdminGetsEveryAdminPermissionButNotTheDevelopmentWildcard()
    {
        var admin = await GrantsAsync("ADMIN");
        var system = await GrantsAsync("SYSTEM_ADMIN");

        Assert.Equal(admin.Where(g => !g.StartsWith("FULL_ACCESS=", StringComparison.Ordinal)), system);
        Assert.DoesNotContain(system, g => g.StartsWith("FULL_ACCESS=", StringComparison.Ordinal));
        Assert.All(system, grant => Assert.EndsWith("=All", grant, StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task OnlyTheOrganizationRolesCanOpenTheOrganizationScreens()
    {
        var holders = await QueryAsync("""
            SELECT r.code || '=' || rp.scope
            FROM role_permission rp
            JOIN role r ON r.id = rp.role_id
            JOIN permission p ON p.id = rp.permission_id
            WHERE p.code = 'ORGANIZATION_READ'
            ORDER BY r.code
            """);

        Assert.Equal(
            ["ADMIN=All", "BRANCH_MANAGER=Branch", "DEPT_MANAGER=Dept", "MANAGER=Dept", "SYSTEM_ADMIN=All"],
            holders);
    }

    [PostgresFact]
    public async Task OnlyTheManagersCanManageJobPositionsAndTheSystemAdministratorCannot()
    {
        var holders = await QueryAsync("""
            SELECT r.code || '=' || rp.scope
            FROM role_permission rp
            JOIN role r ON r.id = rp.role_id
            JOIN permission p ON p.id = rp.permission_id
            WHERE p.code = 'JOB_POSITION_MANAGE'
            ORDER BY r.code
            """);

        Assert.Equal(["BRANCH_MANAGER=Branch", "DEPT_MANAGER=Dept", "MANAGER=Dept"], holders);
    }

    /// <summary>SCR-STRAT-05 needs every feedback state on the Q4 HDV employee targets, and the demo employee needs a target.</summary>
    [PostgresFact]
    public async Task EmployeeTargetFeedbackCoversConfirmedCommentedAndNotViewed()
    {
        // The rollback test in this class can drop and recreate target_assignment_feedback empty; reseed (idempotent).
        await using (var scope = fixture.Provider!.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
        }

        var states = await QueryAsync("""
            SELECT e.employee_code || '=' || COALESCE(f.type::text, 'NOT_VIEWED')
            FROM target_assignment ta
            JOIN employee e ON e.id = ta.employee_id
            JOIN kpi_definition k ON k.id = ta.kpi_definition_id AND k.code = 'HDV'
            LEFT JOIN target_assignment_feedback f ON f.target_assignment_id = ta.id
            WHERE ta.evaluation_period_id = '5eed0000-0000-0000-000f-000000000003' AND ta.status = 'ACTIVE'
              AND ta.parent_assignment_id IS NOT NULL
            ORDER BY e.employee_code
            """);

        Assert.Equal(["NV004=CONFIRM", "NV005=COMMENT", "NV006=NOT_VIEWED", "NV007=NOT_VIEWED"], states);
        Assert.Equal(["NV012=COMMENT"], await QueryAsync("""
            SELECT e.employee_code || '=' || f.type::text
            FROM target_assignment_feedback f JOIN employee e ON e.id = f.employee_id
            WHERE f.target_assignment_id = '5eed0000-0000-0000-0015-00000000000f'
            """));
    }

    /// <summary>The "submit appeal" form lists the caller's latest rejected submission; the demo employee needs one to try it.</summary>
    [PostgresFact]
    public async Task DemoEmployeeHasOneRejectedSubmissionThatCanBeAppealed()
    {
        await using (var scope = fixture.Provider!.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
        }

        Assert.Equal(["DEMO-REJECTED-NV012|NV012|REJECTED|REJECTED|NV011|0"], await QueryAsync("""
            SELECT w.code || '|' || submitter.employee_code || '|' || w.status::text || '|' || s.status::text
                || '|' || verifier.employee_code || '|' || (SELECT count(*) FROM appeal a WHERE a.result_submission_id = s.id)
            FROM work w
            JOIN result_submission s ON s.work_id = w.id
            JOIN employee submitter ON submitter.id = s.submitter_employee_id
            JOIN verification v ON v.result_submission_id = s.id AND v.decision = 'REJECTED'
            JOIN employee verifier ON verifier.id = v.verifier_employee_id
            WHERE w.code = 'DEMO-REJECTED-NV012' AND now() - v.verified_at < interval '48 hours'
            """));
    }

    [PostgresFact]
    public async Task SeedingAgainIsIdempotentAndRemovesRolesThatAnEarlierSeedAddedByMistake()
    {
        // Simulate a database seeded by the previous layout: NV002 also holds MANAGER and VERIFIER.
        await ExecuteAsync("""
            INSERT INTO user_role (account_id, role_id)
            SELECT a.id, r.id FROM account a
            JOIN employee e ON e.id = a.employee_id AND e.employee_code = 'NV002'
            JOIN role r ON r.code IN ('MANAGER', 'VERIFIER')
            ON CONFLICT DO NOTHING
            """);
        Assert.Equal(["3"], await QueryAsync("""
            SELECT count(*)::text FROM user_role ur JOIN account a ON a.id = ur.account_id
            JOIN employee e ON e.id = a.employee_id WHERE e.employee_code = 'NV002'
            """));

        await using (var scope = fixture.Provider!.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
        }

        var roles = await QueryAsync("""
            SELECT e.employee_code || '=' || r.code
            FROM user_role ur JOIN account a ON a.id = ur.account_id
            JOIN employee e ON e.id = a.employee_id JOIN role r ON r.id = ur.role_id
            WHERE e.employee_code LIKE 'NV%' ORDER BY e.employee_code
            """);
        Assert.Equal(ExpectedAccountRoles, roles);
        Assert.Equal(["21"], await QueryAsync("SELECT count(*)::text FROM account a JOIN employee e ON e.id = a.employee_id WHERE e.employee_code LIKE 'NV%'"));
    }

    [PostgresFact]
    public async Task AnAdministratorsExtraRoleOnANonDemoAccountIsNotTouched()
    {
        var account = Guid.NewGuid();
        var suffix = account.ToString("N")[..8];
        await ExecuteAsync($"""
            INSERT INTO employee (id, employee_code, full_name, email, department_id, job_position_id)
              SELECT '{account}', 'X{suffix}', 'X', 'x{suffix}@test.local', department_id, job_position_id FROM employee WHERE employee_code = 'NV004';
            INSERT INTO account (id, employee_id, username, password_hash) VALUES ('{account}', '{account}', 'x{suffix}', 'h');
            INSERT INTO user_role (account_id, role_id) SELECT '{account}', id FROM role WHERE code IN ('EMPLOYEE', 'VERIFIER');
            """);

        await using (var scope = fixture.Provider!.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
        }

        Assert.Equal(["2"], await QueryAsync($"SELECT count(*)::text FROM user_role WHERE account_id = '{account}'"));
    }

    [PostgresFact]
    public async Task TheGrantMigrationCopiesTheSourceRolesAndCanBeRolledBackWithoutLosingOrganizationRead()
    {
        await using var scope = fixture.Provider!.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<AppDbContext>().GetService<IMigrator>();
        var before = await GrantsAsync("DEPT_MANAGER");

        await migrator.MigrateAsync("20261027120000_SeedOrganizationReadAccess");
        var afterRollback = await GrantsAsync("DEPT_MANAGER");
        await migrator.MigrateAsync();
        var reapplied = await GrantsAsync("DEPT_MANAGER");

        Assert.Equal(["ORGANIZATION_READ=Dept"], afterRollback);
        Assert.Equal(before, reapplied);
    }

    [Fact]
    public void TheLatestMigrationsAreInOrder()
    {
        using var scope = fixture.Provider?.CreateScope() ?? new ServiceCollection().AddDataAccess("Host=unused").BuildServiceProvider().CreateScope();
        var migrations = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetMigrations().ToList();

        Assert.Equal("20261110000000_GrantTransactionImportToManagersAndQa", migrations[^1]);
        Assert.Equal("20261109182557_AddImportBatchError", migrations[^2]);
        Assert.Equal("20261105160000_AddRiskEventScreens", migrations[^3]);
        Assert.Equal("20261105151226_AddSystemParameters", migrations[^4]);
        Assert.Equal("20261105150000_FixInvalidateProgressUsesRawValue", migrations[^5]);
        Assert.Equal("20261105094144_AddQaItemCode", migrations[^6]);
        Assert.Equal("20261105082939_AddQualityAssurance", migrations[^7]);
        Assert.Equal("20261104171857_AddKpiCatalogFields", migrations[^8]);
        Assert.Equal("20261104160611_AddWorkTransactionReconcile", migrations[^9]);
        Assert.Equal("20261104153505_AddTransactionImport", migrations[^10]);
        Assert.Equal("20261104121610_AddNotifications", migrations[^11]);
        Assert.Equal("20261104100000_RemoveReportsComplaintsSidebarItem", migrations[^12]);
        Assert.Equal("20261104090751_AddContributionWeightedActual", migrations[^13]);
        Assert.Equal("20261104090000_GrantAppealDecisionToBranchManager", migrations[^14]);
        Assert.Equal("20261103171853_AddScoreEntryAdjustmentKind", migrations[^15]);
        Assert.Equal("20261103132840_AddObjectiveWeightAndKpiRequestApproval", migrations[^16]);
        Assert.Equal("20261103112541_UpdateTargetChildrenSumTriggerBySplitDimension", migrations[^17]);
        Assert.Equal("20261102134713_AddDataImportSupport", migrations[^18]);
        Assert.Equal("20261102112929_AddWorkTypeProductMultiplier", migrations[^19]);
        Assert.Equal("20261101165132_AddEvaluationProfileApproval", migrations[^20]);
    }

    [PostgresFact]
    public async Task TheSidebarMigrationRemovesTheRetiredItemAndItsRouteAndKeepsTheRest()
    {
        await using var scope = fixture.Provider!.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<AppDbContext>().GetService<IMigrator>();
        await migrator.MigrateAsync("20261104090000_GrantAppealDecisionToBranchManager");
        await ExecuteAsync("""
            DELETE FROM sidebar_configurations WHERE role IN ('sidebar-old', 'sidebar-clean');
            INSERT INTO sidebar_configurations (id, role, item_ids, version, routes, created_at, is_deleted)
            VALUES
              (gen_random_uuid(), 'sidebar-old', ARRAY['overview','reportsComplaints','appeals'], 3,
               '{"reportsComplaints": "/home", "appeals": "/appeals"}'::jsonb, now(), false),
              (gen_random_uuid(), 'sidebar-clean', ARRAY['overview','appeals'], 5, '{"appeals": "/appeals"}'::jsonb, now(), false);
            """);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        Assert.Equal(["appeals,overview|3|appeals"], await QueryAsync("""
            SELECT (SELECT string_agg(i, ',' ORDER BY i) FROM unnest(item_ids) i) || '|' || version || '|'
                   || (SELECT string_agg(k, ',') FROM jsonb_object_keys(routes) k)
            FROM sidebar_configurations WHERE role = 'sidebar-old'
            """));
        Assert.Equal(["appeals,overview|5|appeals"], await QueryAsync("""
            SELECT (SELECT string_agg(i, ',' ORDER BY i) FROM unnest(item_ids) i) || '|' || version || '|'
                   || (SELECT string_agg(k, ',') FROM jsonb_object_keys(routes) k)
            FROM sidebar_configurations WHERE role = 'sidebar-clean'
            """));
        await ExecuteAsync("DELETE FROM sidebar_configurations WHERE role IN ('sidebar-old', 'sidebar-clean')");
    }

    /// <summary>The role's grants as <c>CODE=Scope</c>, ordered.</summary>
    private async Task<List<string>> GrantsAsync(string role)
    {
        var rows = await QueryAsync($"""
            SELECT p.code || '=' || rp.scope
            FROM role_permission rp
            JOIN role r ON r.id = rp.role_id
            JOIN permission p ON p.id = rp.permission_id
            WHERE r.code = '{role}'
            """);
        return [.. rows.Order(StringComparer.Ordinal)];
    }
    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ReconcileDemoDataIsSeededAndExistingWorkTypeKpisDefaultToCount()
    {
        // The rollback test in this class drops and recreates these tables empty; reseed (idempotent).
        await using (var scope = fixture.Provider!.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync("demo-password-hash");
        }

        var statuses = await QueryAsync("""
            SELECT status || '/' || COALESCE(reason, '-') || '=' || count(*)
            FROM work_transaction GROUP BY status, reason ORDER BY 1
            """);
        Assert.Equal(
            ["IN_REVIEW/-=1", "MATCHED/-=2", "NOT_MATCHED/EMPLOYEE_MISMATCH=1", "NOT_MATCHED/PRODUCT_MISMATCH=1"],
            statuses);

        var sources = await QueryAsync("SELECT value_source || '=' || count(*) FROM work_type_kpi GROUP BY value_source");
        Assert.Equal(["COUNT=10"], sources);

        var credited = await QueryAsync("""
            SELECT w.status || '/' || count(i.id)
            FROM work w
            JOIN work_transaction wt ON wt.work_id = w.id
            JOIN verified_work_result vwr ON vwr.work_transaction_id = wt.id
            JOIN work_result_item i ON i.verified_work_result_id = vwr.id
            GROUP BY w.status
            """);
        Assert.Equal(["DONE/2"], credited);
    }

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
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
