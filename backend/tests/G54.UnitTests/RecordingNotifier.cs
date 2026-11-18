using G54.BLL.Services;

namespace G54.UnitTests;

/// <summary>Collects the notifications a service raises instead of storing and pushing them.</summary>
public sealed class RecordingNotifier : INotifier
{
    public List<NotificationDraft> Drafts { get; } = [];

    public Task PublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default)
    {
        Drafts.AddRange(drafts);
        return Task.CompletedTask;
    }

    public Task TryPublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default)
        => PublishAsync(drafts, cancellationToken);
}
