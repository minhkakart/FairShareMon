using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class EventMemberSettlement
{
    public EventMemberSettlement()
    {
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EventMemberSettlement>(entity =>
        {
            entity.ToTable("event_member_settlements");

            // Composite PK - a per-(event, member) state row needs no surrogate key/uuid (settled-per-member OQ1a).
            entity.HasKey(settlement => new { settlement.EventId, settlement.MemberId });

            entity.Property(settlement => settlement.EventId).HasColumnName("event_id");
            entity.Property(settlement => settlement.MemberId).HasColumnName("member_id");

            entity.Property(settlement => settlement.IsSettled)
                .HasColumnName("is_settled")
                .HasDefaultValue(false);

            entity.Property(settlement => settlement.SettledAt).HasColumnName("settled_at");

            entity.Property(settlement => settlement.CreatedAt).HasColumnName("created_at");
            entity.Property(settlement => settlement.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            // Delete the event => drop its settlement rows (cascade); the member FK restricts (settled-per-member OQ1a).
            entity.HasOne(settlement => settlement.Event)
                .WithMany()
                .HasForeignKey(settlement => settlement.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(settlement => settlement.Member)
                .WithMany()
                .HasForeignKey(settlement => settlement.MemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });
}
