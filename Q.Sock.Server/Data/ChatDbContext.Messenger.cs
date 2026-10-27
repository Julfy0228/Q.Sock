using Microsoft.EntityFrameworkCore;
using Q.Sock.Server.Entities;

namespace Q.Sock.Server.Data;

public sealed partial class ChatDbContext
{
    public DbSet<GuildEntity> Guilds => Set<GuildEntity>();
    public DbSet<GuildMemberEntity> GuildMembers => Set<GuildMemberEntity>();
    public DbSet<ChatEntity> Chats => Set<ChatEntity>();
    public DbSet<ChatParticipantEntity> ChatParticipants => Set<ChatParticipantEntity>();
    public DbSet<AttachmentEntity> Attachments => Set<AttachmentEntity>();
    public DbSet<MessageReceiptEntity> MessageReceipts => Set<MessageReceiptEntity>();
    public DbSet<FriendshipEntity> Friendships => Set<FriendshipEntity>();
    public DbSet<CallEntity> Calls => Set<CallEntity>();
    public DbSet<CallParticipantEntity> CallParticipants => Set<CallParticipantEntity>();
    public DbSet<IntegrationIdentityEntity> ExternalIdentities => Set<IntegrationIdentityEntity>();
    public DbSet<LaunchNonceEntity> LaunchNonces => Set<LaunchNonceEntity>();
    public DbSet<AuditEntity> AuditLog => Set<AuditEntity>();
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();

    private static void ConfigureMessenger(ModelBuilder b)
    {
        b.Entity<GuildEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<GuildMemberEntity>().HasKey(x => new { x.GuildId, x.UserId });
        b.Entity<GuildMemberEntity>()
            .HasOne<GuildEntity>()
            .WithMany()
            .HasForeignKey(x => x.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<GuildMemberEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatEntity>().HasIndex(x => x.DirectKey).IsUnique();
        b.Entity<ChatEntity>()
            .HasOne<GuildEntity>()
            .WithMany()
            .HasForeignKey(x => x.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChatParticipantEntity>().HasKey(x => new { x.ChatId, x.UserId });
        b.Entity<ChatParticipantEntity>()
            .HasOne<ChatEntity>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatParticipantEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<MessageEntity>()
            .HasOne<ChatEntity>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<MessageEntity>().HasIndex(x => new { x.SenderId, x.ClientOperationId }).IsUnique();
        b.Entity<MessageEntity>().HasIndex(x => new { x.ChatId, x.SentAt });
        b.Entity<AttachmentEntity>()
            .HasOne<MessageEntity>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<AttachmentEntity>()
            .HasOne<ChatEntity>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<AttachmentEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UploadedBy)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<MessageReceiptEntity>().HasKey(x => new { x.MessageId, x.UserId });
        b.Entity<MessageReceiptEntity>()
            .HasOne<MessageEntity>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<MessageReceiptEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<FriendshipEntity>().HasIndex(x => x.PairKey).IsUnique();
        b.Entity<FriendshipEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.FromId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<FriendshipEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.ToId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<CallEntity>()
            .HasOne<ChatEntity>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<CallEntity>().HasIndex(x => x.ChatId).IsUnique().HasFilter("\"Status\" = 'live'");
        b.Entity<CallEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.OrganizerId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<CallParticipantEntity>().HasKey(x => new { x.CallId, x.UserId });
        b.Entity<CallParticipantEntity>()
            .HasOne<CallEntity>()
            .WithMany()
            .HasForeignKey(x => x.CallId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<CallParticipantEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<IntegrationIdentityEntity>()
            .HasIndex(x => new { x.Provider, x.Subject })
            .IsUnique();
        b.Entity<IntegrationIdentityEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Entity<LaunchNonceEntity>().Property(x => x.Id).HasMaxLength(128);
        b.Entity<AuditEntity>().HasIndex(x => x.AtUtc);
    }
}
