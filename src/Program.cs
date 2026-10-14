using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOpenApi();

        builder.Services.AddDbContext<TaskDbContext>(context =>
            context.UseNpgsql(builder.Configuration.GetConnectionString("Db")));

        builder.Services.Configure<RabbitMqOptions>(
            builder.Configuration.GetSection("RabbitMq"));

        builder.Services.AddSingleton<IRabbitMqService, RabbitMqService>();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();

            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
            context.Database.Migrate();
        }

        app.MapPost("/tasks", async (
            CreateTaskRequest request,
            TaskDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.BadRequest(new
                {
                    error = "Title cannot be empty."
                });
            }

            if (request.Title.Length > 200)
            {
                return Results.BadRequest(new
                {
                    error = "Title cannot exceed 200 characters."
                });
            }

            var priority = request.Priority ?? Priority.Medium;

            if (!Enum.IsDefined(typeof(Priority), priority))
            {
                return Results.BadRequest(new
                {
                    error = "Invalid priority."
                });
            }

            var task = new TaskItem
            {
                Id = Guid.NewGuid(),
                Title = request.Title,
                IsCompleted = false,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = null,
                Priority = priority
            };

            db.Tasks.Add(task);

            await db.SaveChangesAsync(cancellationToken);

            return Results.Created($"/tasks/{task.Id}", task);
        });

        app.MapGet("/tasks", async (
            TaskDbContext db,
            CancellationToken cancellationToken) =>
        {
            var tasks = await db.Tasks.ToListAsync(cancellationToken);

            return Results.Ok(tasks);
        });

        app.MapPut("/tasks/{id:guid}/complete", async (
            Guid id,
            TaskDbContext db,
            IRabbitMqService rabbit,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var task = await db.Tasks.FindAsync(
                id, cancellationToken);

            if (task is null)
                return Results.NotFound();

            if (task.IsCompleted)
                return Results.Conflict(new
                {
                    error = "Task is already completed."
                });

            task.IsCompleted = true;
            task.CompletedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            var taskCompletedEvent = new TaskCompletedEvent(
                id, task.Title, task.CompletedAt.Value, task.Priority);

            try
            {
                await rabbit.PublishTaskCompletedAsync(
                    taskCompletedEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to publish task completion event for {TaskId}",
                    task.Id);
            }

            return Results.Ok(task);
        });

        app.MapDelete("/tasks/{id:guid}", async (
            Guid id,
            TaskDbContext db,
            CancellationToken cancellationToken) =>
        {
            var task = await db.Tasks.FindAsync(
                id, cancellationToken);

            if (task is null)
                return Results.NotFound();

            db.Tasks.Remove(task);

            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });

        app.Run();
    }
}

