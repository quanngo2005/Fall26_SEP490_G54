namespace G54.BLL.Services;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventMessage, CancellationToken cancellationToken = default)
        where TEvent : class;
}
