using System.Linq.Expressions;
using G54.DAL.Common;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL.Repositories;

public class Repository<TEntity>(AppDbContext dbContext) : IRepository<TEntity> where TEntity : class
{
    protected AppDbContext DbContext { get; } = dbContext;
    protected DbSet<TEntity> DbSet { get; } = dbContext.Set<TEntity>();

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync([id], cancellationToken);
    }

    public virtual async Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.AsNoTracking().ToListAsync(cancellationToken);
    }

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.AsNoTracking().Where(predicate).ToListAsync(cancellationToken);
    }

    public virtual async Task<PagedResult<TEntity>> GetPagedAsync(
        int pageIndex,
        int pageSize,
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        CancellationToken cancellationToken = default)
    {
        var safePageIndex = pageIndex <= 0 ? 1 : pageIndex;
        var safePageSize = pageSize <= 0 ? 20 : pageSize;

        var query = DbSet.AsNoTracking();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        if (orderBy is not null)
        {
            query = orderBy(query);
        }

        var items = await query
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>
        {
            Items = items,
            PageIndex = safePageIndex,
            PageSize = safePageSize,
            TotalCount = totalCount,
        };
    }

    public virtual async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(predicate, cancellationToken);
    }

    public virtual async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default)
    {
        return predicate is null
            ? await DbSet.CountAsync(cancellationToken)
            : await DbSet.CountAsync(predicate, cancellationToken);
    }

    public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var entry = await DbSet.AddAsync(entity, cancellationToken);
        return entry.Entity;
    }

    public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await DbSet.AddRangeAsync(entities, cancellationToken);
    }

    public virtual void Update(TEntity entity)
    {
        DbSet.Update(entity);
    }

    public virtual void Remove(TEntity entity)
    {
        if (entity is ISoftDeletable softDeletable)
        {
            softDeletable.IsDeleted = true;
            DbSet.Update(entity);
        }
        else
        {
            DbSet.Remove(entity);
        }
    }

    public virtual void RemoveRange(IEnumerable<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            Remove(entity);
        }
    }

    public virtual IQueryable<TEntity> Query(bool asNoTracking = true)
    {
        return asNoTracking ? DbSet.AsNoTracking() : DbSet.AsQueryable();
    }
}
