using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Entities;

namespace Q.Sock.Server.Data;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Name).IsRequired().HasMaxLength(32);
            entity.HasIndex(user => user.Name).IsUnique();
        });

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Text).IsRequired().HasMaxLength(2000);
            entity.Property(message => message.SentAt).IsRequired();
            entity.HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(message => message.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
