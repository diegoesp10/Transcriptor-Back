using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Persistence;

public sealed class MeetingTranscriberDbContext(DbContextOptions<MeetingTranscriberDbContext> options) : DbContext(options)
{
    public DbSet<ClientEntity> Clients => Set<ClientEntity>();
    public DbSet<CaseFileEntity> CaseFiles => Set<CaseFileEntity>();
    public DbSet<MeetingEntity> Meetings => Set<MeetingEntity>();
    public DbSet<MeetingParticipantEntity> MeetingParticipants => Set<MeetingParticipantEntity>();
    public DbSet<TranscriptSegmentEntity> TranscriptSegments => Set<TranscriptSegmentEntity>();
    public DbSet<ErrorLogEntity> ErrorLogs => Set<ErrorLogEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("transcriber");
        Configure<ClientEntity>(model, "Clients");
        Configure<CaseFileEntity>(model, "CaseFiles");
        Configure<MeetingEntity>(model, "Meetings");
        Configure<MeetingParticipantEntity>(model, "MeetingParticipants");
        Configure<TranscriptSegmentEntity>(model, "TranscriptSegments");
        Configure<ErrorLogEntity>(model, "ErrorLogs");

        var error = model.Entity<ErrorLogEntity>();
        error.Property(x => x.Source).HasConversion<string>().HasMaxLength(30);
        error.Property(x => x.Code).HasConversion<string>().HasMaxLength(100);
        error.Property(x => x.Message).HasMaxLength(500).IsRequired();
        error.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        error.Property(x => x.Endpoint).HasMaxLength(300);
        error.Property(x => x.ProviderCode).HasMaxLength(120);
        error.Property(x => x.ProviderRequestId).HasMaxLength(120);
        error.Property(x => x.ExceptionType).HasMaxLength(300);
        error.Property(x => x.StackTrace).HasMaxLength(4000);
        error.HasIndex(x => x.CreatedAt);
        error.HasIndex(x => new { x.MeetingId, x.CreatedAt });
        error.HasIndex(x => x.CorrelationId);

        var client = model.Entity<ClientEntity>();
        client.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        client.Property(x => x.Email).HasMaxLength(320);
        client.Property(x => x.ExternalReference).HasMaxLength(100);
        var caseFile = model.Entity<CaseFileEntity>();
        caseFile.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        caseFile.Property(x => x.Title).HasMaxLength(300).IsRequired();
        caseFile.HasIndex(x => x.Reference).IsUnique();
        caseFile.HasOne(x => x.Client).WithMany(x => x.CaseFiles).HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);

        var meeting = model.Entity<MeetingEntity>();
        meeting.Property(x => x.Title).HasMaxLength(300);
        meeting.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        meeting.Property(x => x.Language).HasMaxLength(2).IsRequired();
        meeting.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        meeting.Property(x => x.ErrorCode).HasMaxLength(100);
        meeting.Property(x => x.RetryCount).HasDefaultValue(0);
        meeting.HasIndex(x => new { x.Status, x.CreatedAt });
        meeting.HasIndex(x => x.TemporaryExpiresAt);
        meeting.HasOne(x => x.CaseFile).WithMany(x => x.Meetings).HasForeignKey(x => x.CaseFileId).OnDelete(DeleteBehavior.Restrict);
        meeting.ToTable("Meetings", table => table.HasCheckConstraint("CK_Meetings_Progress",
            "[CompletedChunks] >= 0 AND [TotalChunks] >= [CompletedChunks]"));
        meeting.ToTable("Meetings", table => table.HasCheckConstraint("CK_Meetings_RetryCount", "[RetryCount] >= 0"));

        var participant = model.Entity<MeetingParticipantEntity>();
        participant.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        participant.Property(x => x.Role).HasMaxLength(50);
        participant.Property(x => x.SpeakerLabel).HasMaxLength(100);
        participant.HasOne(x => x.Meeting).WithMany(x => x.Participants).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);

        var segment = model.Entity<TranscriptSegmentEntity>();
        segment.Property(x => x.Text).IsRequired();
        segment.Property(x => x.Speaker).HasMaxLength(100);
        segment.HasIndex(x => new { x.MeetingId, x.Sequence }).IsUnique();
        segment.HasOne(x => x.Meeting).WithMany(x => x.Segments).HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
        segment.ToTable("TranscriptSegments", table => table.HasCheckConstraint("CK_TranscriptSegments_Timing",
            "[StartSeconds] >= 0 AND [EndSeconds] >= [StartSeconds] AND [Sequence] >= 0"));
    }

    private static void Configure<TEntity>(ModelBuilder model, string table) where TEntity : BaseEntity
    {
        var entity = model.Entity<TEntity>();
        entity.ToTable(table);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.RowVersion).IsRowVersion();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        return base.SaveChangesAsync(cancellationToken);
    }
}
