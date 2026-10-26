using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class WorkOverdueBackgroundServiceTests
{
    // 2026-03-01 18:00 UTC is already 2026-03-02 01:00 at UTC+7, the business calendar the sweep uses.
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_DoesNotSweep_WhenDisabled()
    {
        var work = new RecordingWorkService();
        var service = CreateService(work, new Dictionary<string, string?> { ["WorkOverdue:Enabled"] = "false" });

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(work.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_SweepsWithTheUtcPlusSevenToday_OnStartup()
    {
        var work = new RecordingWorkService();
        var service = CreateService(work);

        await service.StartAsync(CancellationToken.None);
        await work.FirstSweep.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 3, 2), Assert.Single(work.Calls));
    }

    [Fact]
    public async Task ExecuteAsync_KeepsTheHostHealthy_WhenASweepFails()
    {
        var work = new RecordingWorkService { Fail = true };
        var service = CreateService(work);

        await service.StartAsync(CancellationToken.None);
        await work.FirstSweep.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The failure is logged inside the sweep; stopping must still complete without rethrowing it.
        await service.StopAsync(CancellationToken.None);
        Assert.Single(work.Calls);
    }

    private static WorkOverdueBackgroundService CreateService(RecordingWorkService work, Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();
        var provider = new ServiceCollection()
            .AddScoped<IWorkService>(_ => work)
            .BuildServiceProvider();

        return new WorkOverdueBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            new FixedTimeProvider(Now),
            NullLogger<WorkOverdueBackgroundService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingWorkService : IWorkService
    {
        public List<DateOnly> Calls { get; } = [];

        public TaskCompletionSource FirstSweep { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Fail { get; init; }

        public Task<int> MarkOverdueAsync(DateOnly today, CancellationToken cancellationToken = default)
        {
            Calls.Add(today);
            FirstSweep.TrySetResult();
            if (Fail)
            {
                throw new InvalidOperationException("database unavailable");
            }

            return Task.FromResult(0);
        }

        public Task<WorkDetailDto> CreateWorkAsync(G54.BLL.Dtos.Work.CreateWorkRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the overdue sweep.");

        public Task<G54.BLL.Common.Models.PagedResult<WorkListItemDto>> GetWorksAsync(Guid accountId, WorkFilterDto filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the overdue sweep.");

        public Task<WorkDetailDto?> GetByIdAsync(Guid id, Guid accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the overdue sweep.");

        public Task<WorkDetailDto> ChangeStatusAsync(Guid id, Guid userId, WorkStatusDto dto, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the overdue sweep.");
    }
}
