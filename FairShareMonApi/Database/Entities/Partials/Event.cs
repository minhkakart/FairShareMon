using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class Event
{
    /// <summary>Max length of an event name (mirrors the expense name cap, OQ9).</summary>
    public const int NameMaxLength = 200;

    /// <summary>Max length of the optional description (OQ9).</summary>
    public const int DescriptionMaxLength = 1000;

    public Event()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events", table =>
                // The codebase's second CHECK (after ck_shares_amount_non_negative): the range is
                // always non-empty; end<start on input is rejected earlier by validation (1001), OQ1/OQ12.
                table.HasCheckConstraint("ck_events_date_range", "end_date >= start_date"));

            entity.HasKey(evt => evt.Id);
            entity.Property(evt => evt.Id).HasColumnName("id");

            entity.Property(evt => evt.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(evt => evt.Uuid).IsUnique();

            entity.Property(evt => evt.UserId).HasColumnName("user_id");
            entity.HasIndex(evt => evt.UserId);

            entity.Property(evt => evt.Name).HasColumnName("name").HasMaxLength(NameMaxLength);

            entity.Property(evt => evt.Description).HasColumnName("description").HasMaxLength(DescriptionMaxLength);

            entity.Property(evt => evt.StartDate).HasColumnName("start_date");

            entity.Property(evt => evt.EndDate).HasColumnName("end_date");

            entity.Property(evt => evt.IsClosed)
                .HasColumnName("is_closed")
                .HasDefaultValue(false);

            entity.Property(evt => evt.ClosedAt).HasColumnName("closed_at");

            entity.Property(evt => evt.CreatedAt).HasColumnName("created_at");
            entity.Property(evt => evt.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            // Composite index serving the default list sort (start_date DESC, OQ10).
            entity.HasIndex(evt => new { evt.UserId, evt.StartDate });

            entity.HasOne(evt => evt.User)
                .WithMany()
                .HasForeignKey(evt => evt.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
