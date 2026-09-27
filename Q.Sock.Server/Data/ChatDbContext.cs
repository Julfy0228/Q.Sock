using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Q.Sock.Server.Entities;

namespace Q.Sock.Server.Data;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options)
    : IdentityDbContext<UserEntity, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserEntity>()
            .Property(user => user.UserName)
            .IsRequired()
            .HasMaxLength(32);

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
