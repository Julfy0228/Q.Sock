namespace Q.Sock.Server.Plugins.Moodle;

public sealed class CourseLinkEntity
{
    public Guid Id { get; set; }
    public string ConnectionId { get; set; } = "";
    public long ExternalCourseId { get; set; }
    public Guid GuildId { get; set; }
    public Guid ChatId { get; set; }
    public Guid RoomId { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
