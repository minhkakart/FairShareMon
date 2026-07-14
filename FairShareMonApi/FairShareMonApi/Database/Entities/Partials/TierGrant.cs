using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class TierGrant
{
    /// <summary>Max length of the offline payment reference.</summary>
    public const int ReferenceMaxLength = 255;

    /// <summary>Max length of the admin note.</summary>
    public const int NoteMaxLength = 500;

    /// <summary>Max length of the ISO currency code.</summary>
    public const int CurrencyMaxLength = 3;

    /// <summary>Name of the DB CHECK constraint enforcing <c>amount &gt;= 0</c> (§4.3, OQ5).</summary>
    public const string AmountCheckConstraintName = "ck_tier_grants_amount_non_negative";

    public TierGrant()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<TierGrant>(entity =>
        {
            // Money is non-negative at the DB level (§4.3), mirroring shares.amount.
            entity.ToTable("tier_grants", table => table.HasCheckConstraint(AmountCheckConstraintName, "amount >= 0"));

            entity.HasKey(grant => grant.Id);
            entity.Property(grant => grant.Id).HasColumnName("id");

            entity.Property(grant => grant.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(grant => grant.Uuid).IsUnique();

            // Plain value, no navigation FK (immutable trail like audit_logs, OQ5).
            entity.Property(grant => grant.UserId).HasColumnName("user_id");

            entity.Property(grant => grant.UserUsername).HasColumnName("user_username").HasMaxLength(32);

            entity.Property(grant => grant.Tier).HasColumnName("tier").HasMaxLength(16);

            entity.Property(grant => grant.Action).HasColumnName("action").HasMaxLength(16);

            entity.Property(grant => grant.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");

            entity.Property(grant => grant.Currency).HasColumnName("currency").HasMaxLength(CurrencyMaxLength);

            entity.Property(grant => grant.Reference).HasColumnName("reference").HasMaxLength(ReferenceMaxLength);

            entity.Property(grant => grant.Note).HasColumnName("note").HasMaxLength(NoteMaxLength);

            // Plain value, no navigation FK (immutable trail like audit_logs, OQ5).
            entity.Property(grant => grant.GrantedByUserId).HasColumnName("granted_by_user_id");

            entity.Property(grant => grant.GrantedByUsername).HasColumnName("granted_by_username").HasMaxLength(32);

            entity.Property(grant => grant.CreatedAt).HasColumnName("created_at");
            entity.Property(grant => grant.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            // Revenue range scan (OQ14) and per-user grant history (OQ7/OQ9).
            entity.HasIndex(grant => grant.CreatedAt);
            entity.HasIndex(grant => new { grant.UserId, grant.CreatedAt });
        });
}
