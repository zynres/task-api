using Microsoft.Extensions.Options;
using System.Text.Json;
using RabbitMQ.Client;

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin";
}

public sealed class RabbitMqService : IRabbitMqService, IAsyncDisposable
{
    private readonly RabbitMqOptions options;
    private readonly ILogger<RabbitMqService> logger;
    private readonly SemaphoreSlim initLock = new(1, 1);

    private IConnection? connection;
    private IChannel? channel;

    public RabbitMqService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqService> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    private async Task EnsureConnectedAsync(
     CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true } && connection is { IsOpen: true })
            return;

        await initLock.WaitAsync(cancellationToken);

        try
        {
            if (channel is { IsOpen: true } && connection is { IsOpen: true })
                return;

            if (connection is not { IsOpen: true })
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync();
                    channel = null;
                }
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                    connection = null;
                }

                var factory = new ConnectionFactory
                {
                    HostName = options.Host,
                    Port = options.Port,
                    UserName = options.Username,
                    Password = options.Password
                };

                try
                {
                    connection = await factory.CreateConnectionAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "RabbitMQ unavailable on attempt {count}. Retrying in 5s...");
                    return;
                }
            }

            if (channel is not { IsOpen: true })
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync();
                }

                channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

                await channel.ExchangeDeclareAsync(
                    exchange: "task.events",
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: cancellationToken);
            }
        }
        finally
        {
            initLock.Release();
        }
    }

    public async Task PublishTaskCompletedAsync(
        TaskCompletedEvent message,
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        var json = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                taskId = message.TaskId,
                title = message.Title,
                completedAt = message.CompletedAt,
                priority = message.Priority.ToString()
            });

        await channel!.BasicPublishAsync(
            exchange: "task.events",
            routingKey: "task.completed",
            mandatory: false,
            basicProperties: new BasicProperties(),
            body: json,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (channel is not null)
            await channel.DisposeAsync();

        if (connection is not null)
            await connection.DisposeAsync();

        initLock.Dispose();
    }
}
