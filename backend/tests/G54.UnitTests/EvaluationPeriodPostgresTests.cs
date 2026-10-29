using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.EvaluationPeriod;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace G54.UnitTests;

/// <summary>
/// Runs <see cref="EvaluationPeriodService"/> on migrated PostgreSQL: the unique <c>code</c> index and the
/// <c>period_status</c> enum mapping only exist there. Skips when no Postgres is available (see <see cref="TestPostgres"/>).
/// </summary>
public sealed class EvaluationPeriodPostgresTests(DemoSeedDatabaseFixture fixture) : IClassFixture<DemoSeedDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);

    private static CreateEvaluationPeriodRequestDto Request(string code) =>
        new(code, "Năm 2026", "YEAR", null, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    private async Task<T> RunAsync<T>(Func<EvaluationPeriodService, AppDbContext, Task<T>> action)
    {
        await using var scope = fixture.Provider!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(new EvaluationPeriodService(db, new FixedTime(Now)), db);
    }

    [PostgresFact]
    public async Task Create_ThenClose_PersistsStatuses()
    {
        var code = $"E2E-{Guid.NewGuid():N}"[..20];

        var created = await RunAsync((service, _) => service.CreateAsync(Request(code)));
        var closed = await RunAsync((service, _) => service.CloseAsync(created.Id));
        var stored = await RunAsync((_, db) => db.EvaluationPeriods.AsNoTracking().SingleAsync(p => p.Id == created.Id));

        Assert.Equal("OPEN", created.Status);
        Assert.Equal("IN_REVIEW", closed.Status);
        Assert.Equal(PeriodStatus.InReview, stored.Status);
    }

    [PostgresFact]
    public async Task Create_DuplicateCode_IsConflict()
    {
        var code = $"E2E-{Guid.NewGuid():N}"[..20];
        await RunAsync((service, _) => service.CreateAsync(Request(code)));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => RunAsync((service, _) => service.CreateAsync(Request(code))));

        Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
