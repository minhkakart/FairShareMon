using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class AuditLog
{
    public AuditLog()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");

            entity.HasKey(log => log.Id);
            entity.Property(log => log.Id).HasColumnName("id");

            entity.Property(log => log.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(log => log.Uuid).IsUnique();

            entity.Property(log => log.ActorUserId).HasColumnName("actor_user_id");

            entity.Property(log => log.EntityType).HasColumnName("entity_type").HasConversion<int>();

            entity.Property(log => log.EntityUuid).HasColumnName("entity_uuid").HasMaxLength(64);

            entity.Property(log => log.ExpenseUuid).HasColumnName("expense_uuid").HasMaxLength(64);

            entity.Property(log => log.Action).HasColumnName("action").HasConversion<int>();

            entity.Property(log => log.BeforeData).HasColumnName("before_data").HasColumnType("longtext");

            entity.Property(log => log.AfterData).HasColumnName("after_data").HasColumnType("longtext");

            entity.Property(log => log.CreatedAt).HasColumnName("created_at");
            entity.Property(log => log.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            // Serves the per-expense, time-ordered history read (OQ17).
            entity.HasIndex(log => new { log.ActorUserId, log.ExpenseUuid, log.CreatedAt });

            // Only the actor has an FK; entity_uuid/expense_uuid are plain values so the log survives
            // the hard-delete of its expense/share (OQ9).
            entity.HasOne(log => log.ActorUser)
                .WithMany()
                .HasForeignKey(log => log.ActorUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
