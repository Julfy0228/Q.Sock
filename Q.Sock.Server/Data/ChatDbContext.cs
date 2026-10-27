using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Entities;

namespace Q.Sock.Server.Data;

public sealed partial class ChatDbContext(DbContextOptions<ChatDbContext> options)
    : IdentityDbContext<UserEntity, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();
    public DbSet<DeviceSessionEntity> DeviceSessions => Set<DeviceSessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureMessenger(modelBuilder);

        modelBuilder
            .Entity<UserEntity>()
            .Property(user => user.UserName)
            .IsRequired()
            .HasMaxLength(32);

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Text).IsRequired().HasMaxLength(2000);
            entity.Property(message => message.SentAt).IsRequired();
            entity
                .HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(message => message.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeviceSessionEntity>(entity =>
        {
            entity.HasKey(session => session.Id);
            entity.Property(session => session.DeviceId).IsRequired().HasMaxLength(128);
            entity.Property(session => session.DeviceName).IsRequired().HasMaxLength(128);
            entity.Property(session => session.RefreshTokenHash).IsRequired().HasMaxLength(64);
            entity.HasIndex(session => session.RefreshTokenHash).IsUnique();
            entity.HasIndex(session => new { session.UserId, session.RevokedAtUtc });
            entity
                .HasOne<UserEntity>()
                .WithMany()
                .HasForeignKey(session => session.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
