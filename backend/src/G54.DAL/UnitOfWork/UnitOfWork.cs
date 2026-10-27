using System.Collections.Concurrent;
using G54.DAL.Entities;
using G54.DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace G54.DAL.Uow;

public sealed class UnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    private readonly ConcurrentDictionary<Type, object> _repositories = new();
    private IDbContextTransaction? _currentTransaction;

    public IWorkRepository Works => (IWorkRepository)_repositories.GetOrAdd(
        typeof(Work),
        _ => new WorkRepository(dbContext));

    public IRepository<WorkType> WorkTypes => Repository<WorkType>();

    public IRepository<Employee> Employees => Repository<Employee>();

    public IRepository<BusinessCampaign> BusinessCampaigns => Repository<BusinessCampaign>();

    public IRepository<CampaignWorkType> CampaignWorkTypes => Repository<CampaignWorkType>();

    public IRepository<Participant> Participants => Repository<Participant>();

    public IRepository<TaskHandover> TaskHandovers => Repository<TaskHandover>();

    public IRepository<ResultSubmission> ResultSubmissions => Repository<ResultSubmission>();

    public IRepository<Evidence> Evidences => Repository<Evidence>();

    public IRepository<Verification> Verifications => Repository<Verification>();
    public IRepository<WorkTypeKpi> WorkTypeKpis => Repository<WorkTypeKpi>();
    public IRepository<KpiDefinition> KpiDefinitions => Repository<KpiDefinition>();
    public IRepository<Department> Departments => Repository<Department>();
    public IRepository<JobPosition> JobPositions => Repository<JobPosition>();

    public ITargetAssignmentRepository TargetAssignments => (ITargetAssignmentRepository)_repositories.GetOrAdd(
        typeof(TargetAssignment), _ => new TargetAssignmentRepository(dbContext));

    public IEvaluationPeriodRepository EvaluationPeriods => (IEvaluationPeriodRepository)_repositories.GetOrAdd(
        typeof(EvaluationPeriod), _ => new EvaluationPeriodRepository(dbContext));

    public IRepository<ActualProgress> ActualProgresses => Repository<ActualProgress>();

    public ISystemParameterRepository SystemParameters => (ISystemParameterRepository)_repositories.GetOrAdd(
        typeof(SystemParameter), _ => new SystemParameterRepository(dbContext));

    public IAdminAccessRepository AdminAccess => (IAdminAccessRepository)_repositories.GetOrAdd(
        typeof(AdminAccessRepository), _ => new AdminAccessRepository(dbContext));

    public IRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        return (IRepository<TEntity>)_repositories.GetOrAdd(
            typeof(TEntity),
            _ => new Repository<TEntity>(dbContext));
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is not null)
        {
            return _currentTransaction;
        }

        _currentTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        return _currentTransaction;
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            throw new InvalidOperationException("No active transaction to commit.");
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction is null)
        {
            return;
        }

        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await action(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await action(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private async Task DisposeTransactionAsync()
    {
        if (_currentTransaction is not null)
        {
            await _currentTransaction.DisposeAsync();
            _currentTransaction = null;
        }
    }

    public void Dispose()
    {
        _currentTransaction?.Dispose();
        _currentTransaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeTransactionAsync();
    }
}
