using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using System.Net.Http.Json;
using System.Net;
using Moq;

public class CompleteTaskIntegrationTest
{
    [Fact]
    public async Task CompleteTask_ShouldPublishEvent()
    {
        var rabbitMock = new Mock<IRabbitMqService>();

        rabbitMock
            .Setup(x => x.PublishTaskCompletedAsync(
                It.IsAny<TaskCompletedEvent>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var connection =
            new SqliteConnection("DataSource=InMemoryTestDb;Mode=Memory;Cache=Shared");

        await connection.OpenAsync();

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(
                    Path.GetFullPath(
                        Path.Combine(
                            AppContext.BaseDirectory,
                            "../../../../src")));

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TaskDbContext>();
                    services.RemoveAll<DbContextOptions<TaskDbContext>>();
                    services.RemoveAll<
                        IDbContextOptionsConfiguration<TaskDbContext>>();

                    services.AddDbContext<TaskDbContext>(options =>
                        options.UseSqlite(connection));

                    services.RemoveAll<IRabbitMqService>();
                    services.AddSingleton<IRabbitMqService>(
                        rabbitMock.Object);
                });
            });

        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider
            .GetRequiredService<TaskDbContext>();

        await db.Database.EnsureCreatedAsync();

        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/tasks",
            new
            {
                title = "Buy milk",
                priority = (byte)Priority.High
            });
        var body = await createResponse.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.Created,
            createResponse.StatusCode);

        var createdTask = await createResponse.Content
            .ReadFromJsonAsync<TaskItem>();

        Assert.NotNull(createdTask);
        Assert.False(createdTask.IsCompleted);
        Assert.Null(createdTask.CompletedAt);

        var completeResponse = await client.PutAsync(
            $"/tasks/{createdTask.Id}/complete",
            null);

        Assert.Equal(
            HttpStatusCode.OK,
            completeResponse.StatusCode);

        rabbitMock.Verify(
            x => x.PublishTaskCompletedAsync(
                It.Is<TaskCompletedEvent>(e =>
                    e.TaskId == createdTask.Id &&
                    e.Title == "Buy milk" &&
                    e.Priority == Priority.High),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
