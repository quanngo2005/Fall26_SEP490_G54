using G54.DAL.Entities;
using G54.DAL.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace G54.DAL.Uow;

public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    IWorkRepository Works { get; }
    IRepository<WorkType> WorkTypes { get; }
    IRepository<Employee> Employees { get; }
    IRepository<BusinessCampaign> BusinessCampaigns { get; }
    IRepository<CampaignWorkType> CampaignWorkTypes { get; }
    IRepository<Participant> Participants { get; }
    IRepository<TaskHandover> TaskHandovers { get; }
    IRepository<ResultSubmission> ResultSubmissions { get; }
    IRepository<Evidence> Evidences { get; }
    IRepository<Verification> Verifications { get; }
    IRepository<WorkTypeKpi> WorkTypeKpis { get; }
    IRepository<KpiDefinition> KpiDefinitions { get; }
    IRepository<Department> Departments { get; }
    IRepository<JobPosition> JobPositions { get; }
    ITargetAssignmentRepository TargetAssignments { get; }
    IEvaluationPeriodRepository EvaluationPeriods { get; }
    IRepository<ActualProgress> ActualProgresses { get; }
    ISystemParameterRepository SystemParameters { get; }
    IAdminAccessRepository AdminAccess { get; }

    IRepository<TEntity> Repository<TEntity>() where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);

    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken = default);
}
