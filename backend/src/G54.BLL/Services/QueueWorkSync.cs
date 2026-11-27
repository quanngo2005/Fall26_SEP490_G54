using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Queue;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

internal sealed class QueueWorkSync(IServiceScopeFactory scopes, IConfiguration config, ILogger<QueueWorkSync> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogRetry = LoggerMessage.Define(LogLevel.Warning,
        new EventId(5401, "QueueSyncRetry"), "Queue activity synchronization will retry");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(config["Queue:Origin"])) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<QueueConnection>();
                var pending = await queue.RequestAsync<QueueHistory[]>(HttpMethod.Get, "/internal/pending", null, stoppingToken);
                foreach (var visit in pending)
                {
                    using var itemScope = scopes.CreateScope();
                    var projection = itemScope.ServiceProvider.GetRequiredService<QueueWorkProjection>();
                    await projection.ApplyAsync(visit, stoppingToken);
                    await queue.RequestAsync<int>(HttpMethod.Post, $"/internal/ack/{visit.Id}/{visit.Version}", null, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Common.Exceptions.BusinessRuleException ex) { LogRetry(logger, ex); }
            catch (DbUpdateException ex) { LogRetry(logger, ex); }
            catch (System.Data.Common.DbException ex) { LogRetry(logger, ex); }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}

internal sealed class QueueWorkProjection(IUnitOfWork unit)
{
    internal async Task ApplyAsync(QueueHistory visit, CancellationToken ct)
    {
        var work = await unit.Works.GetByIdAsync(visit.Id, ct);
        if (work == null)
        {
            var type = await unit.WorkTypes.Query().SingleAsync(t => t.Code == visit.WorkType, ct);
            var creation = visit.Events.First(e => (e.Action == "start" && e.Service != "advice") || e.Action == "convert");
            WorkStateMachine.TryGetNextStatus(WorkStatus.New, WorkAction.Start, out var status);
            work = new Work
            {
                Id = visit.Id,
                Code = $"Q-{visit.Id:N}",
                Title = $"{type.Name} – {visit.Number}",
                CreatedBy = visit.WorkOwner,
                AssigneeEmployeeId = visit.WorkOwner,
                WorkTypeId = type.Id,
                Priority = WorkPriority.Medium,
                Status = status,
                AssignedAt = creation.At,
                UpdatedAt = creation.At,
                PlannedStartDate = DateOnly.FromDateTime(creation.At.ToOffset(TimeSpan.FromHours(7)).DateTime)
            };
            await unit.Works.AddAsync(work, ct);
        }
        var ids = visit.Events.Select(e => e.Id).ToArray();
        var existing = await unit.Repository<WorkActivity>().Query().Where(a => a.WorkId == visit.Id && ids.Contains(a.Id))
            .Select(a => a.Id).ToArrayAsync(ct);
        foreach (var e in visit.Events.Where(e => !existing.Contains(e.Id)))
        {
            var label = e.Action switch
            {
                "issued" => "Cấp số",
                "called" => "Gọi số",
                "recall" => "Gọi lại",
                "start" => "Bắt đầu phục vụ",
                "convert" => "Xác nhận sản phẩm",
                "transfer" => "Chuyển quầy",
                "waiting" => "Chờ phục vụ",
                "finish" => "Kết thúc lượt",
                "rated" => "Khách đã đánh giá",
                "skipped" => "Khách bỏ qua đánh giá",
                "absent" => "Khách vắng",
                _ => e.Action
            };
            var service = e.Service switch
            {
                "deposit" => "Nộp tiền mặt",
                "withdrawal" => "Rút tiền mặt",
                "transfer" => "Chuyển tiền",
                "account" => "Mở tài khoản",
                "savings" => "Gửi tiết kiệm",
                "card" => "Mở thẻ tín dụng",
                "advice" => "Tư vấn sản phẩm cá nhân",
                _ => e.Service
            };
            var desk = e.Counter.HasValue ? $" · Quầy {e.Counter:00}" : string.Empty;
            await unit.Repository<WorkActivity>().AddAsync(new WorkActivity
            {
                Id = e.Id,
                WorkId = visit.Id,
                ActorEmployeeId = e.Actor,
                Action = $"QUEUE_{e.Action.ToUpperInvariant()}",
                Message = $"{label} · {visit.Number} · {service}{desk}",
                CreatedAt = e.At
            }, ct);
        }
        // One SaveChanges transaction commits the Work and its missing events together. Primary keys make redelivery safe.
        await unit.SaveChangesAsync(ct);
    }
}
