using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class QrCorrelationCode
{
    /// <summary>Max length of the <see cref="Code"/> column (OQ1's ~9-char format, roomy).</summary>
    public const int CodeMaxLength = 16;

    /// <summary>Fixed, always-present prefix (OQ1) - trivially greppable/recognizable in a bank statement.</summary>
    public const string CodePrefix = "FSM";

    /// <summary>Number of random characters appended after <see cref="CodePrefix"/> (OQ1).</summary>
    public const int CodeRandomLength = 6;

    /// <summary>
    /// 30-ish symbol alphabet: A-Z2-9 minus the visually-ambiguous O/0/I/1/L (OQ1), so a hand-typed/
    /// read-back code (some bank apps show the memo back to the payer) is unambiguous.
    /// </summary>
    public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public QrCorrelationCode()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<QrCorrelationCode>(entity =>
        {
            entity.ToTable("qr_correlation_codes", table => table.HasCheckConstraint(
                "ck_qr_correlation_codes_amount_non_negative", "expected_amount_snapshot >= 0"));

            entity.HasKey(code => code.Id);
            entity.Property(code => code.Id).HasColumnName("id");

            entity.Property(code => code.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(code => code.Uuid).IsUnique();

            entity.Property(code => code.UserId).HasColumnName("user_id");
            entity.HasIndex(code => code.UserId);

            entity.Property(code => code.EventId).HasColumnName("event_id");
            entity.HasIndex(code => code.EventId);

            entity.Property(code => code.MemberId).HasColumnName("member_id");

            entity.Property(code => code.ExpenseId).HasColumnName("expense_id");
            entity.HasIndex(code => code.ExpenseId);

            entity.Property(code => code.Code).HasColumnName("code").HasMaxLength(CodeMaxLength);
            // The anonymous lookup key the webhook path uses - mirrors EventShareLink.Token.
            entity.HasIndex(code => code.Code).IsUnique();

            entity.Property(code => code.ExpectedAmountSnapshot)
                .HasColumnName("expected_amount_snapshot")
                .HasColumnType("decimal(18,2)");

            entity.Property(code => code.ExpiresAt).HasColumnName("expires_at");

            entity.Property(code => code.CreatedAt).HasColumnName("created_at");
            entity.Property(code => code.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(code => code.User)
                .WithMany()
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(code => code.Event)
                .WithMany()
                .HasForeignKey(code => code.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(code => code.Member)
                .WithMany()
                .HasForeignKey(code => code.MemberId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(code => code.Expense)
                .WithMany()
                .HasForeignKey(code => code.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
