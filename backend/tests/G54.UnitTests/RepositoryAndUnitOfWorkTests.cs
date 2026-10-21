using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Repositories;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class RepositoryAndUnitOfWorkTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GenericRepository_AddAndGetById_Succeeds()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new Repository<Work>(context);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WRK-TEST-01",
            Title = "Test Task 01",
            AssigneeEmployeeId = Guid.NewGuid(),
            CreatedBy = Guid.NewGuid(),
            Status = WorkStatus.New,
            Priority = WorkPriority.Medium,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await repository.AddAsync(work);
        await context.SaveChangesAsync();

        var retrieved = await repository.GetByIdAsync(work.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("WRK-TEST-01", retrieved.Code);

        var exists = await repository.AnyAsync(w => w.Code == "WRK-TEST-01");
        Assert.True(exists);

        var count = await repository.CountAsync(w => w.Status == WorkStatus.New);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GenericRepository_GetPagedAsync_CalculatesCorrectPagination()
    {
        using var context = CreateInMemoryDbContext();
        var repository = new Repository<Work>(context);

        var works = Enumerable.Range(1, 15).Select(i => new Work
        {
            Id = Guid.NewGuid(),
            Code = $"WRK-PAGED-{i:D2}",
            Title = $"Paged Task {i}",
            AssigneeEmployeeId = Guid.NewGuid(),
            CreatedBy = Guid.NewGuid(),
            Status = WorkStatus.InProgress,
            Priority = WorkPriority.High,
            UpdatedAt = DateTimeOffset.UtcNow,
        }).ToList();

        await repository.AddRangeAsync(works);
        await context.SaveChangesAsync();

        var pagedResult = await repository.GetPagedAsync(
            pageIndex: 2,
            pageSize: 5,
            predicate: w => w.Status == WorkStatus.InProgress,
            orderBy: q => q.OrderBy(w => w.Code));

        Assert.Equal(5, pagedResult.Items.Count);
        Assert.Equal(2, pagedResult.PageIndex);
        Assert.Equal(5, pagedResult.PageSize);
        Assert.Equal(15, pagedResult.TotalCount);
        Assert.Equal(3, pagedResult.TotalPages);
        Assert.True(pagedResult.HasPreviousPage);
        Assert.True(pagedResult.HasNextPage);
        Assert.Equal("WRK-PAGED-06", pagedResult.Items[0].Code);
    }

    [Fact]
    public async Task WorkRepository_KanbanBoardAndCodeCheck_WorksAsExpected()
    {
        using var context = CreateInMemoryDbContext();
        var workRepo = new WorkRepository(context);
        var assigneeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = assigneeId,
            EmployeeCode = "EMP001",
            FullName = "John Doe",
            Email = "john@bank.com",
            Status = EmployeeStatus.Active,
        };
        await context.Employees.AddAsync(employee);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WRK-KB-01",
            Title = "Kanban Task",
            AssigneeEmployeeId = assigneeId,
            AssigneeEmployee = employee,
            CreatedBy = assigneeId,
            Status = WorkStatus.New,
            Priority = WorkPriority.Critical,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await workRepo.AddAsync(work);
        await context.SaveChangesAsync();

        var exists = await workRepo.ExistsByCodeAsync("WRK-KB-01");
        Assert.True(exists);

        var kanbanBoard = await workRepo.GetKanbanBoardAsync(
            assigneeEmployeeId: assigneeId,
            campaignId: null,
            status: WorkStatus.New,
            pageIndex: 1,
            pageSize: 10);

        Assert.Single(kanbanBoard.Items);
        Assert.Equal("WRK-KB-01", kanbanBoard.Items[0].Code);
    }

    [Fact]
    public async Task UnitOfWork_ExposesRepositoriesAndSavesChanges()
    {
        using var context = CreateInMemoryDbContext();
        var uow = new UnitOfWork(context);

        Assert.NotNull(uow.Works);
        Assert.NotNull(uow.Participants);
        Assert.NotNull(uow.TaskHandovers);
        Assert.NotNull(uow.ResultSubmissions);
        Assert.NotNull(uow.Evidences);
        Assert.NotNull(uow.Verifications);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WRK-UOW-01",
            Title = "UoW Task",
            AssigneeEmployeeId = Guid.NewGuid(),
            CreatedBy = Guid.NewGuid(),
            Status = WorkStatus.New,
            Priority = WorkPriority.Low,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await uow.Works.AddAsync(work);
        var savedCount = await uow.SaveChangesAsync();

        Assert.Equal(1, savedCount);
        var found = await uow.Works.GetByIdAsync(work.Id);
        Assert.NotNull(found);
    }
}
