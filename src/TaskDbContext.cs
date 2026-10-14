using Microsoft.EntityFrameworkCore;

public class TaskDbContext : DbContext
{
    public DbSet<TaskItem> Tasks { get; set; }

    public TaskDbContext(DbContextOptions<TaskDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
}
