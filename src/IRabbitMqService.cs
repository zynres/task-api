public interface IRabbitMqService
{
    Task PublishTaskCompletedAsync(
        TaskCompletedEvent message,
        CancellationToken cancellationToken = default);
}
