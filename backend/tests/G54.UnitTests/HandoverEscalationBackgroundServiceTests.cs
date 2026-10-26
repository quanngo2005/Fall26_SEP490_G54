using G54.BLL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class HandoverEscalationBackgroundServiceTests
{
    private static HandoverEscalationBackgroundService Create(RecordingEscalation escalation, params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection().AddScoped<IHandoverEscalationService>(_ => escalation).BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new HandoverEscalationBackgroundService(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<HandoverEscalationBackgroundService>.Instance);
    }

    private static async Task RunFirstSweepAsync(HandoverEscalationBackgroundService service, RecordingEscalation escalation)
    {
        await service.StartAsync(CancellationToken.None);
        await escalation.FirstSweep.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Execute_DoesNothingWhenDisabled()
    {
        var escalation = new RecordingEscalation();
        using var service = Create(escalation, ("HandoverEscalation:Enabled", "false"));

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(escalation.Slas);
    }

    [Fact]
    public async Task Execute_SweepsImmediatelyWithTheConfiguredSla()
    {
        var escalation = new RecordingEscalation { Result = 3 };
        using var service = Create(escalation, ("HandoverEscalation:SlaHours", "6"), ("HandoverEscalation:IntervalMinutes", "15"));

        await RunFirstSweepAsync(service, escalation);

        Assert.Equal(TimeSpan.FromHours(6), Assert.Single(escalation.Slas));
    }

    [Fact]
    public async Task Execute_UsesFourHoursByDefault()
    {
        var escalation = new RecordingEscalation();
        using var service = Create(escalation);

        await RunFirstSweepAsync(service, escalation);

        Assert.Equal(TimeSpan.FromHours(4), Assert.Single(escalation.Slas));
    }

    [Fact]
    public async Task Execute_FallsBackToTheDefaultsForInvalidSettings()
    {
        var escalation = new RecordingEscalation();
        using var service = Create(escalation, ("HandoverEscalation:SlaHours", "0"), ("HandoverEscalation:IntervalMinutes", "-1"));

        await RunFirstSweepAsync(service, escalation);

        Assert.Equal(TimeSpan.FromHours(4), Assert.Single(escalation.Slas));
    }

    [Fact]
    public async Task Execute_SurvivesAFailingSweepAndStopsCleanly()
    {
        var escalation = new RecordingEscalation { Failure = new InvalidOperationException("db down") };
        using var service = Create(escalation);

        await RunFirstSweepAsync(service, escalation);

        Assert.Single(escalation.Slas);
        Assert.Null(service.ExecuteTask?.Exception);
    }

    private sealed class RecordingEscalation : IHandoverEscalationService
    {
        public List<TimeSpan> Slas { get; } = [];

        public int Result { get; set; }

        public Exception? Failure { get; set; }

        public TaskCompletionSource FirstSweep { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> EscalateOverdueAsync(TimeSpan sla, CancellationToken cancellationToken = default)
        {
            Slas.Add(sla);
            FirstSweep.TrySetResult();
            return Failure is null ? Task.FromResult(Result) : throw Failure;
        }
    }
}
