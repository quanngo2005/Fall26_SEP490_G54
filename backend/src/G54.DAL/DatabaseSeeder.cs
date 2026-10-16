using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public sealed class DatabaseSeeder(AppDbContext dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    public Task SeedAsync(CancellationToken cancellationToken = default)
        => SeedAsync(null, cancellationToken);

    public async Task SeedAsync(
        Func<string, string>? passwordHasher,
        CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var defaultPasswordHash = passwordHasher != null
            ? passwordHasher("Password@123")
            : "pbkdf2-sha256$600000$WvQJvL1Z2rIe4uQo3XpM8g==$3VvD5b1s2A4d6F8h0J2l4N6p8R0t2V4x6Z8b0D2f4H8=";

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // 1. Departments
        var departments = await SeedDepartmentsAsync(now, cancellationToken);

        // 2. Evaluation Profiles & Job Positions
        var jobPositions = await SeedEvaluationProfilesAndPositionsAsync(now, departments, cancellationToken);

        // 3. Roles & Permissions
        var roles = await SeedRolesAndPermissionsAsync(now, cancellationToken);

        // 4. Employees & Accounts
        await SeedEmployeesAndAccountsAsync(now, departments, jobPositions, roles, defaultPasswordHash, cancellationToken);

        // 5. Workflows & WorkTypes
        await SeedWorkflowsAndWorkTypesAsync(now, cancellationToken);

        // 6. KpiGroups & KpiDefinitions
        await SeedKpisAsync(now, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Dictionary<string, Department>> SeedDepartmentsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var deptDefs = new (string Code, string Name, string? ParentCode, DepartmentLevelType Level)[]
        {
            ("BOD", "Ban Giám Đốc Chi Nhánh", null, DepartmentLevelType.Branch),
            ("KHCN", "Phòng Khách Hàng Cá Nhân", "BOD", DepartmentLevelType.Department),
            ("OPS", "Phòng Dịch Vụ Khách Hàng & Vận Hành", "BOD", DepartmentLevelType.Department),
            ("QA", "Phòng Quản Lý Chất Lượng & Tuân Thủ", "BOD", DepartmentLevelType.Department),
            ("HR", "Phòng Nhân Sự & Đào Tạo", "BOD", DepartmentLevelType.Department),
        };

        var result = new Dictionary<string, Department>(StringComparer.OrdinalIgnoreCase);

        // First pass: insert or get departments without parent references
        foreach (var def in deptDefs)
        {
            var dept = await _dbContext.Departments
                .SingleOrDefaultAsync(d => d.Code == def.Code, cancellationToken);

            if (dept is null)
            {
                dept = new Department
                {
                    Code = def.Code,
                    Name = def.Name,
                    Description = $"{def.Name} - Chi nhánh ngân hàng.",
                    LevelType = def.Level,
                    Status = RecordStatus.Active,
                    LastSyncAt = now,
                    UpdatedAt = now,
                };
                _dbContext.Departments.Add(dept);
            }
            result[def.Code] = dept;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Second pass: link parent departments
        foreach (var def in deptDefs)
        {
            if (def.ParentCode != null && result.TryGetValue(def.ParentCode, out var parentDept))
            {
                var child = result[def.Code];
                if (child.ParentDepartmentId != parentDept.Id)
                {
                    child.ParentDepartmentId = parentDept.Id;
                    child.UpdatedAt = now;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<Dictionary<string, JobPosition>> SeedEvaluationProfilesAndPositionsAsync(
        DateTimeOffset now,
        Dictionary<string, Department> departments,
        CancellationToken cancellationToken)
    {
        var epDefs = new (string Code, string Name)[]
        {
            ("EP_BOD", "Khung đánh giá năng lực Ban Giám Đốc"),
            ("EP_KHCN", "Khung đánh giá năng lực Khách hàng Cá nhân"),
            ("EP_OPS", "Khung đánh giá năng lực Vận hành & Dịch vụ quầy"),
            ("EP_QA", "Khung đánh giá năng lực Kiểm soát & Tuân thủ"),
            ("EP_HR", "Khung đánh giá năng lực Nhân sự & Đào tạo"),
        };

        var profiles = new Dictionary<string, EvaluationProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in epDefs)
        {
            var ep = await _dbContext.EvaluationProfiles
                .SingleOrDefaultAsync(e => e.Code == def.Code, cancellationToken);
            if (ep is null)
            {
                ep = new EvaluationProfile
                {
                    Code = def.Code,
                    Name = def.Name,
                    Description = $"{def.Name} phiên bản chuẩn hóa.",
                    Version = 1,
                    EffectiveFrom = DateOnly.FromDateTime(now.UtcDateTime),
                    Status = EvaluationProfileStatus.Draft,
                    UpdatedAt = now,
                };
                _dbContext.EvaluationProfiles.Add(ep);
            }
            profiles[def.Code] = ep;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var posDefs = new (string Code, string Title, string EpCode, string DeptCode)[]
        {
            ("POS_DIR", "Giám Đốc Chi Nhánh", "EP_BOD", "BOD"),
            ("POS_DEPT_HEAD_KHCN", "Trưởng Phòng Khách Hàng Cá Nhân", "EP_KHCN", "KHCN"),
            ("POS_PRM_VIP", "Chuyên Viên Quản Lý KH VIP (Priority Banking)", "EP_KHCN", "KHCN"),
            ("POS_RBO", "Chuyên Viên Khách Hàng Cá Nhân", "EP_KHCN", "KHCN"),
            ("POS_TELLER", "Giao Dịch Viên Quầy", "EP_OPS", "OPS"),
            ("POS_QA_OFFICER", "Chuyên Viên Kiểm Soát Tuân Thủ", "EP_QA", "QA"),
            ("POS_HR_OFFICER", "Chuyên Viên Nhân Sự & KPI", "EP_HR", "HR"),
        };

        var positions = new Dictionary<string, JobPosition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in posDefs)
        {
            var pos = await _dbContext.JobPositions
                .SingleOrDefaultAsync(p => p.Code == def.Code, cancellationToken);
            if (pos is null)
            {
                pos = new JobPosition
                {
                    Code = def.Code,
                    Title = def.Title,
                    Description = $"Chức danh {def.Title}.",
                    EvaluationProfileId = profiles[def.EpCode].Id,
                    Status = RecordStatus.Active,
                    LastSyncAt = now,
                    UpdatedAt = now,
                };
                _dbContext.JobPositions.Add(pos);
            }
            positions[def.Code] = pos;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return positions;
    }

    private async Task<Dictionary<string, Role>> SeedRolesAndPermissionsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var roleDefs = new (string Code, string Name, string Description)[]
        {
            ("ADMIN", "Quản Trị Viên Hệ Thống", "Toàn quyền quản trị hệ thống."),
            ("BRANCH_MANAGER", "Giám Đốc Chi Nhánh", "Xem và điều hành toàn bộ dữ liệu chi nhánh."),
            ("DEPT_MANAGER", "Trưởng Phòng Nghiệp Vụ", "Quản lý mục tiêu, phân công và thẩm định kết quả phòng ban."),
            ("EMPLOYEE", "Chuyên Viên / Nhân Viên", "Thực thi công việc và báo cáo kết quả KPI cá nhân."),
            ("QA_COMPLIANCE", "Chuyên Viên Giám Sát Tuân Thủ", "Kiểm định, giám sát chất lượng và hồ sơ tuân thủ."),
            ("HR_OFFICER", "Cán Bộ Nhân Sự & Tổ Chức", "Quản lý danh bạ, kỳ đánh giá và dữ liệu nhân sự."),
        };

        var roles = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in roleDefs)
        {
            var role = await _dbContext.Roles
                .SingleOrDefaultAsync(r => r.Code == def.Code, cancellationToken);
            if (role is null)
            {
                role = new Role
                {
                    Code = def.Code,
                    Name = def.Name,
                    Description = def.Description,
                    Status = RecordStatus.Active,
                    UpdatedAt = now,
                };
                _dbContext.Roles.Add(role);
            }
            roles[def.Code] = role;
        }

        var permDefs = new (string Code, string Name, string Resource, string Action)[]
        {
            ("FULL_ACCESS", "Full Access", "*", "*"),
            ("DASHBOARD_VIEW", "Xem Dashboard", "dashboard", "view"),
            ("EMPLOYEE_MANAGE", "Quản lý nhân viên", "employee", "manage"),
            ("WORK_MANAGE", "Quản lý công việc", "work", "manage"),
            ("VERIFY_DECISION", "Thẩm định kết quả", "verification", "decide"),
            ("KPI_EVALUATE", "Đánh giá KPI", "kpi", "evaluate"),
        };

        var permissions = new Dictionary<string, PermissionRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in permDefs)
        {
            var perm = await _dbContext.Permissions
                .SingleOrDefaultAsync(p => p.Code == def.Code, cancellationToken);
            if (perm is null)
            {
                perm = new PermissionRecord
                {
                    Code = def.Code,
                    Name = def.Name,
                    Resource = def.Resource,
                    Action = def.Action,
                    Description = def.Name,
                    UpdatedAt = now,
                };
                _dbContext.Permissions.Add(perm);
            }
            permissions[def.Code] = perm;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Role-Permission mapping
        var rolePermMapping = new Dictionary<string, string[]>
        {
            ["ADMIN"] = ["FULL_ACCESS"],
            ["BRANCH_MANAGER"] = ["FULL_ACCESS", "DASHBOARD_VIEW", "EMPLOYEE_MANAGE", "WORK_MANAGE", "VERIFY_DECISION", "KPI_EVALUATE"],
            ["DEPT_MANAGER"] = ["DASHBOARD_VIEW", "EMPLOYEE_MANAGE", "WORK_MANAGE", "VERIFY_DECISION", "KPI_EVALUATE"],
            ["QA_COMPLIANCE"] = ["DASHBOARD_VIEW", "VERIFY_DECISION", "WORK_MANAGE"],
            ["HR_OFFICER"] = ["DASHBOARD_VIEW", "EMPLOYEE_MANAGE", "KPI_EVALUATE"],
            ["EMPLOYEE"] = ["DASHBOARD_VIEW", "WORK_MANAGE"],
        };

        foreach (var (roleCode, permCodes) in rolePermMapping)
        {
            if (!roles.TryGetValue(roleCode, out var role)) continue;

            foreach (var permCode in permCodes)
            {
                if (!permissions.TryGetValue(permCode, out var perm)) continue;

                var exists = await _dbContext.RolePermissions
                    .AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id, cancellationToken);
                if (!exists)
                {
                    _dbContext.RolePermissions.Add(new RolePermissionAssignment
                    {
                        RoleId = role.Id,
                        PermissionId = perm.Id,
                        UpdatedAt = now,
                    });
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return roles;
    }

    private async Task SeedEmployeesAndAccountsAsync(
        DateTimeOffset now,
        Dictionary<string, Department> departments,
        Dictionary<string, JobPosition> jobPositions,
        Dictionary<string, Role> roles,
        string defaultPasswordHash,
        CancellationToken cancellationToken)
    {
        var empDefs = new (string Code, string FullName, string Email, string DeptCode, string PosCode, string? ManagerCode, EmployeeStatus Status, string RoleCode)[]
        {
            ("NV00001", "Trần Thị Thu Hương", "director@bpms.com", "BOD", "POS_DIR", null, EmployeeStatus.Active, "BRANCH_MANAGER"),
            ("NV00002", "Phạm Quốc Bảo", "dept_manager@bpms.com", "KHCN", "POS_DEPT_HEAD_KHCN", "NV00001", EmployeeStatus.Active, "DEPT_MANAGER"),
            ("NV00003", "Lê Hoàng Yến", "prm_vip@bpms.com", "KHCN", "POS_PRM_VIP", "NV00002", EmployeeStatus.Active, "EMPLOYEE"),
            ("NV00004", "Đỗ Minh Khang", "employee@bpms.com", "KHCN", "POS_RBO", "NV00002", EmployeeStatus.Active, "EMPLOYEE"),
            ("NV00005", "Vũ Mai Linh", "resigning_employee@bpms.com", "OPS", "POS_TELLER", "NV00001", EmployeeStatus.OnLeave, "EMPLOYEE"),
            ("NV00006", "Hoàng Trọng Nghĩa", "qa_officer@bpms.com", "QA", "POS_QA_OFFICER", "NV00001", EmployeeStatus.Active, "QA_COMPLIANCE"),
            ("NV00007", "Đặng Thùy Dương", "hr_officer@bpms.com", "HR", "POS_HR_OFFICER", "NV00001", EmployeeStatus.Active, "HR_OFFICER"),
        };

        var seededEmployees = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);

        // Step 1: Create or fetch employees without manager links
        foreach (var def in empDefs)
        {
            var emp = await _dbContext.Employees
                .SingleOrDefaultAsync(e => e.EmployeeCode == def.Code || e.Email == def.Email, cancellationToken);

            if (emp is null)
            {
                emp = new Employee
                {
                    EmployeeCode = def.Code,
                    FullName = def.FullName,
                    Email = def.Email.ToLowerInvariant(),
                    DepartmentId = departments[def.DeptCode].Id,
                    JobPositionId = jobPositions[def.PosCode].Id,
                    Status = def.Status,
                    LastSyncAt = now,
                    UpdatedAt = now,
                };
                _dbContext.Employees.Add(emp);
            }
            seededEmployees[def.Code] = emp;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Step 2: Link managers
        foreach (var def in empDefs)
        {
            if (def.ManagerCode != null && seededEmployees.TryGetValue(def.ManagerCode, out var mgr))
            {
                var emp = seededEmployees[def.Code];
                if (emp.ManagerId != mgr.Id)
                {
                    emp.ManagerId = mgr.Id;
                    emp.UpdatedAt = now;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Step 3: Create Accounts and UserRole assignments
        foreach (var def in empDefs)
        {
            var emp = seededEmployees[def.Code];
            var username = def.Email.ToLowerInvariant();

            var account = await _dbContext.Accounts
                .SingleOrDefaultAsync(a => a.EmployeeId == emp.Id || a.Username == username, cancellationToken);

            if (account is null)
            {
                account = new Account
                {
                    EmployeeId = emp.Id,
                    Username = username,
                    PasswordHash = defaultPasswordHash,
                    IsLocked = false,
                    FailedLoginAttempts = 0,
                    LastSyncAt = now,
                    UpdatedAt = now,
                };
                _dbContext.Accounts.Add(account);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (roles.TryGetValue(def.RoleCode, out var role))
            {
                var hasRole = await _dbContext.UserRoles
                    .AnyAsync(ur => ur.AccountId == account.Id && ur.RoleId == role.Id, cancellationToken);
                if (!hasRole)
                {
                    _dbContext.UserRoles.Add(new UserRole
                    {
                        AccountId = account.Id,
                        RoleId = role.Id,
                        UpdatedAt = now,
                    });
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedWorkflowsAndWorkTypesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var wfDefs = new (string Code, string Name, string Description)[]
        {
            ("WF_DEPOSIT", "Quy trình Mở Sổ Tiết Kiệm & Tiền Gửi", "Quy trình tư vấn, tiếp nhận tiền gửi, định danh AML và mở sổ tiết kiệm tại quầy."),
            ("WF_CREDIT_CARD", "Quy trình Thẩm Định & Phát Hành Thẻ Tín Dụng", "Quy trình thu thập hồ sơ CIC, chấm điểm tín dụng và phê duyệt cấp hạn mức thẻ."),
            ("WF_VIP_CARE", "Quy trình Chăm Sóc & Tư Vấn Khách Hàng VIP", "Quy trình rà soát danh mục AUM, quản lý danh mục tài sản và trải nghiệm đặc quyền Priority."),
            ("WF_COUNTER_TXN", "Quy trình Giao Dịch Thu Chi & Chuyển Tiền Tại Quầy", "Quy trình đối soát chữ ký, sinh trắc học và hạch toán bút toán giao dịch tiền mặt."),
        };

        var workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in wfDefs)
        {
            var wf = await _dbContext.WorkflowDefinitions
                .SingleOrDefaultAsync(w => w.Code == def.Code, cancellationToken);
            if (wf is null)
            {
                wf = new WorkflowDefinition
                {
                    Code = def.Code,
                    Name = def.Name,
                    Description = def.Description,
                    Version = 1,
                    Status = WorkflowDefinitionStatus.Active,
                    UpdatedAt = now,
                };
                _dbContext.WorkflowDefinitions.Add(wf);
            }
            workflows[def.Code] = wf;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var wtDefs = new (string Code, string Name, string WfCode, string Description)[]
        {
            ("WT_DEPOSIT", "Tư vấn & Mở tài khoản Tiết kiệm có kỳ hạn", "WF_DEPOSIT", "Nghiệp vụ mở tài khoản tiền gửi tiết kiệm có kỳ hạn cho khách hàng cá nhân."),
            ("WT_CREDIT_CARD", "Thẩm định & Phát hành Thẻ tín dụng Quốc tế", "WF_CREDIT_CARD", "Nghiệp vụ tiếp nhận hồ sơ, thẩm định hạn mức và phát hành thẻ tín dụng Visa/MasterCard."),
            ("WT_VIP_CARE", "Chăm sóc & Tư vấn Danh mục Đầu tư Khách hàng VIP", "WF_VIP_CARE", "Nghiệp vụ tư vấn tái cơ cấu danh mục tài chính, duy trì số dư AUM cho phân khúc VIP."),
            ("WT_COUNTER_TXN", "Xử lý Giao dịch Nộp/Rút tiền mặt & Chuyển khoản tại quầy", "WF_COUNTER_TXN", "Nghiệp vụ hạch toán nộp/rút tiền mặt, chuyển tiền liên ngân hàng tại quầy giao dịch."),
        };

        foreach (var def in wtDefs)
        {
            var wt = await _dbContext.WorkTypes
                .SingleOrDefaultAsync(w => w.Code == def.Code, cancellationToken);
            if (wt is null)
            {
                wt = new WorkType
                {
                    Code = def.Code,
                    Name = def.Name,
                    Description = def.Description,
                    WorkflowDefinitionId = workflows[def.WfCode].Id,
                    Status = RecordStatus.Active,
                    UpdatedAt = now,
                };
                _dbContext.WorkTypes.Add(wt);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedKpisAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var groupDefs = new (string Code, string Name, string Description)[]
        {
            ("GROUP_SALES", "Nhóm Chỉ Số Kinh Doanh & Bán Hàng (SALES)", "Đo lường quy mô tài sản, doanh số huy động và bán chéo sản phẩm."),
            ("GROUP_OPS", "Nhóm Chỉ Số Vận Hành & Năng Suất (OPERATIONS)", "Đo lường năng suất xử lý giao dịch và thời gian cam kết SLA."),
            ("GROUP_SERVICE", "Nhóm Chỉ Số Chất Lượng Dịch Vụ (SERVICE)", "Đo lường độ hài lòng khách hàng và chất lượng phục vụ."),
            ("GROUP_RISK", "Nhóm Chỉ Số Quản Trị Rủi Ro & Tuân Thủ (RISK)", "Kiểm soát các sai sót vận hành và cảnh báo vi phạm quy chế."),
        };

        var groups = new Dictionary<string, KpiGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in groupDefs)
        {
            var group = await _dbContext.KpiGroups
                .SingleOrDefaultAsync(g => g.Code == def.Code, cancellationToken);
            if (group is null)
            {
                group = new KpiGroup
                {
                    Code = def.Code,
                    Name = def.Name,
                    Description = def.Description,
                    Status = KpiStatus.Active,
                    UpdatedAt = now,
                };
                _dbContext.KpiGroups.Add(group);
            }
            groups[def.Code] = group;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var kpiDefs = new (string Code, string Name, string GroupCode, string Unit, KpiAggregationType AggType, KpiDirection Direction, string Formula, decimal? MaxPercent)[]
        {
            ("KPI_SALES_AUM", "Quy mô huy động vốn ròng bình quân (AUM)", "GROUP_SALES", "VND", KpiAggregationType.Avg, KpiDirection.HigherBetter, "AVG(Net_Deposit)", 150),
            ("KPI_SALES_CARDS", "Số lượng thẻ tín dụng phát hành & kích hoạt mới", "GROUP_SALES", "COUNT", KpiAggregationType.Sum, KpiDirection.HigherBetter, "COUNT(Activated_Cards)", 200),
            ("KPI_OPS_SLA", "Tỷ lệ hoàn thành công việc đúng thời hạn cam kết (SLA Rate)", "GROUP_OPS", "PERCENTAGE", KpiAggregationType.Avg, KpiDirection.HigherBetter, "(OnTime_Tasks / Total_Tasks) * 100", 120),
            ("KPI_OPS_VOLUME", "Sản lượng giao dịch quầy xử lý thành công", "GROUP_OPS", "COUNT", KpiAggregationType.Sum, KpiDirection.HigherBetter, "COUNT(Completed_Txns)", 150),
            ("KPI_SERVICE_CSAT", "Điểm đánh giá chất lượng dịch vụ khách hàng (CSAT)", "GROUP_SERVICE", "POINTS", KpiAggregationType.Avg, KpiDirection.HigherBetter, "AVG(Rating_Score)", 100),
            ("KPI_RISK_COMPLIANCE", "Số vụ việc vi phạm quy chế vận hành & cảnh báo rủi ro", "GROUP_RISK", "COUNT", KpiAggregationType.Sum, KpiDirection.LowerBetter, "COUNT(Violation_Events)", null),
        };

        foreach (var def in kpiDefs)
        {
            var kpi = await _dbContext.KpiDefinitions
                .SingleOrDefaultAsync(k => k.Code == def.Code, cancellationToken);
            if (kpi is null)
            {
                kpi = new KpiDefinition
                {
                    Code = def.Code,
                    Name = def.Name,
                    Description = def.Name,
                    KpiGroupId = groups[def.GroupCode].Id,
                    Unit = def.Unit,
                    AggregationType = def.AggType,
                    Direction = def.Direction,
                    Formula = def.Formula,
                    MaxAchievementPercent = def.MaxPercent,
                    Status = KpiStatus.Active,
                    UpdatedAt = now,
                };
                _dbContext.KpiDefinitions.Add(kpi);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private const string DevSeedResource = "G54.DAL.Scripts.seed_dev_data.sql";

    // Seeded employee -> role. Accounts use the employee email as username.
    private const string DemoUserRolesSql = """
        INSERT INTO user_role (account_id, role_id)
        SELECT a.id, r.id
        FROM (VALUES
            ('NV001', 'MANAGER'), ('NV001', 'VERIFIER'),
            ('NV002', 'MANAGER'), ('NV002', 'VERIFIER'),
            ('NV003', 'MANAGER'), ('NV003', 'VERIFIER'),
            ('NV004', 'EMPLOYEE'), ('NV005', 'EMPLOYEE'), ('NV006', 'EMPLOYEE'),
            ('NV007', 'EMPLOYEE'), ('NV008', 'EMPLOYEE'), ('NV009', 'EMPLOYEE'),
            ('NV010', 'EMPLOYEE')
        ) AS m(employee_code, role_code)
        JOIN employee e ON e.employee_code = m.employee_code
        JOIN account a ON a.employee_id = e.id
        JOIN role r ON r.code = m.role_code
        ON CONFLICT DO NOTHING;
        """;

    /// <summary>
    /// Seeds idempotent development data from sql script.
    /// </summary>
    public async Task SeedFromSqlAsync(string? demoPasswordHash = null, CancellationToken cancellationToken = default)
    {
        var sql = await ReadEmbeddedScriptAsync(DevSeedResource, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        if (!string.IsNullOrEmpty(demoPasswordHash))
        {
            var employees = await _dbContext.Employees
                .Where(item => item.EmployeeCode.StartsWith("NV")
                    && !_dbContext.Accounts.Any(account => account.EmployeeId == item.Id))
                .ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            foreach (var employee in employees)
            {
                _dbContext.Accounts.Add(new Account
                {
                    EmployeeId = employee.Id,
                    Username = employee.Email.ToLowerInvariant(),
                    PasswordHash = demoPasswordHash,
                    LastSyncAt = now,
                    UpdatedAt = now,
                });
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await _dbContext.Database.ExecuteSqlRawAsync(DemoUserRolesSql, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<string> ReadEmbeddedScriptAsync(string resourceName, CancellationToken cancellationToken)
    {
        await using var stream = typeof(DatabaseSeeder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded seed script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
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
                Scope = "All",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
