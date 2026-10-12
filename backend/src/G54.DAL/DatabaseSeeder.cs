using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public sealed class DatabaseSeeder(AppDbContext dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Seeder can be expanded with initial Progest seed data
        if (await _dbContext.Database.CanConnectAsync(cancellationToken))
        {
            // Database connection verified
        }
    }

    public async Task SeedDevelopmentAdminAsync(
        string email,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var employee = await _dbContext.Employees
            .SingleOrDefaultAsync(
                candidate => EF.Functions.ILike(candidate.Email, normalizedEmail),
                cancellationToken);
        if (employee is null)
        {
            var now = DateTimeOffset.UtcNow;
            var department = await _dbContext.Departments
                .SingleOrDefaultAsync(item => item.Code == "DEV_ADMIN", cancellationToken);
            if (department is null)
            {
                department = new Department
                {
                    Code = "DEV_ADMIN",
                    Name = "Development Administration",
                    Description = "Local development bootstrap data.",
                    LevelType = DepartmentLevelType.Department,
                    Status = RecordStatus.Active,
                    LastSyncAt = now,
                    UpdatedAt = now,
                };
                _dbContext.Departments.Add(department);
            }

            var evaluationProfile = await _dbContext.EvaluationProfiles
                .SingleOrDefaultAsync(item => item.Code == "DEV_ADMIN", cancellationToken);
            if (evaluationProfile is null)
            {
                evaluationProfile = new EvaluationProfile
                {
                    Code = "DEV_ADMIN",
                    Name = "Development Administrator",
                    Description = "Local development bootstrap profile.",
                    Version = 1,
                    EffectiveFrom = DateOnly.FromDateTime(now.UtcDateTime),
                    UpdatedAt = now,
                    Status = EvaluationProfileStatus.Draft,
                };
                _dbContext.EvaluationProfiles.Add(evaluationProfile);
            }

            var jobPosition = await _dbContext.JobPositions
                .SingleOrDefaultAsync(item => item.Code == "DEV_ADMIN", cancellationToken);
            if (jobPosition is null)
            {
                jobPosition = new JobPosition
                {
                    Code = "DEV_ADMIN",
                    Title = "Development Administrator",
                    Description = "Local development bootstrap position.",
                    EvaluationProfile = evaluationProfile,
                    LastSyncAt = now,
                    UpdatedAt = now,
                    Status = RecordStatus.Active,
                };
                _dbContext.JobPositions.Add(jobPosition);
            }

            employee = new Employee
            {
                EmployeeCode = "DEV-ADMIN",
                FullName = "Development Administrator",
                Email = normalizedEmail,
                Department = department,
                JobPosition = jobPosition,
                LastSyncAt = now,
                UpdatedAt = now,
                Status = EmployeeStatus.Active,
            };
            _dbContext.Employees.Add(employee);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var account = await _dbContext.Accounts
            .Include(candidate => candidate.UserRoles)
            .SingleOrDefaultAsync(candidate => candidate.EmployeeId == employee.Id, cancellationToken);
        if (account is null)
        {
            var usernameInUse = await _dbContext.Accounts
                .AnyAsync(candidate => candidate.Username == normalizedEmail, cancellationToken);
            if (usernameInUse)
            {
                throw new InvalidOperationException(
                    $"Cannot provision development admin: username '{normalizedEmail}' is already in use.");
            }

            var now = DateTimeOffset.UtcNow;
            account = new Account
            {
                Employee = employee,
                Username = normalizedEmail,
                PasswordHash = passwordHash,
                LastSyncAt = now,
                UpdatedAt = now,
            };
            _dbContext.Accounts.Add(account);
        }

        var adminRole = await _dbContext.Roles
            .SingleOrDefaultAsync(candidate => candidate.Code == "ADMIN", cancellationToken);
        if (adminRole is null)
        {
            adminRole = new Role
            {
                Code = "ADMIN",
                Name = "Administrator",
                Description = "Full access for local development only.",
                UpdatedAt = DateTimeOffset.UtcNow,
                Status = RecordStatus.Active,
            };
            _dbContext.Roles.Add(adminRole);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!await _dbContext.UserRoles.AnyAsync(
                item => item.AccountId == account.Id && item.RoleId == adminRole.Id,
                cancellationToken))
        {
            _dbContext.UserRoles.Add(new UserRole
            {
                Account = account,
                Role = adminRole,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        var fullAccessPermission = await _dbContext.Permissions
            .SingleOrDefaultAsync(item => item.Code == "FULL_ACCESS", cancellationToken);
        if (fullAccessPermission is null)
        {
            fullAccessPermission = new PermissionRecord
            {
                Code = "FULL_ACCESS",
                Name = "Full access",
                Resource = "*",
                Action = "*",
                Description = "Wildcard permission for the local development administrator.",
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            _dbContext.Permissions.Add(fullAccessPermission);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var permissions = await _dbContext.Permissions
            .AsNoTracking()
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var assignedPermissionIds = await _dbContext.RolePermissions
            .Where(item => item.RoleId == adminRole.Id)
            .Select(item => item.PermissionId)
            .ToListAsync(cancellationToken);
        foreach (var permissionId in permissions.Except(assignedPermissionIds))
        {
            _dbContext.RolePermissions.Add(new RolePermissionAssignment
            {
                RoleId = adminRole.Id,
                PermissionId = permissionId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
