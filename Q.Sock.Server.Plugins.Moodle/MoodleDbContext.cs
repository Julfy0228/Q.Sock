using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Q.Sock.Server.Plugins.Moodle;

public sealed class MoodleDbContext(DbContextOptions<MoodleDbContext> options) : DbContext(options)
{
    public DbSet<CourseLinkEntity> CourseLinks => Set<CourseLinkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<CourseLinkEntity>()
            .HasIndex(link => new { link.ConnectionId, link.ExternalCourseId })
            .IsUnique();
        modelBuilder.Entity<CourseLinkEntity>().HasIndex(link => link.GuildId);
        modelBuilder.Entity<CourseLinkEntity>().HasIndex(link => link.ChatId);
    }
}

public sealed class MoodleDbContextFactory : IDesignTimeDbContextFactory<MoodleDbContext>
{
    public MoodleDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<MoodleDbContext>()
                .UseSqlite(
                    "Data Source=qsock-moodle-design.db",
                    options => options.MigrationsHistoryTable("__QsockMoodleMigrations")
                )
                .Options
        );
}
