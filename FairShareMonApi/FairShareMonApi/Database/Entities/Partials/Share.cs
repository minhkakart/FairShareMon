using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class Share
{
    /// <summary>Max length of the optional share note (OQ16).</summary>
    public const int NoteMaxLength = 500;

    /// <summary>Name of the DB CHECK constraint enforcing <c>amount &gt;= 0</c> (§4.3, OQ2) - the codebase's first CHECK.</summary>
    public const string AmountCheckConstraintName = "ck_shares_amount_non_negative";

    public Share()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Share>(entity =>
        {
            // First CHECK constraint in the codebase: money is non-negative at the DB level (§4.3).
            entity.ToTable("shares", table => table.HasCheckConstraint(AmountCheckConstraintName, "amount >= 0"));

            entity.HasKey(share => share.Id);
            entity.Property(share => share.Id).HasColumnName("id");

            entity.Property(share => share.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(share => share.Uuid).IsUnique();

            entity.Property(share => share.ExpenseId).HasColumnName("expense_id");
            entity.HasIndex(share => share.ExpenseId);

            entity.Property(share => share.MemberId).HasColumnName("member_id");

            entity.Property(share => share.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");

            entity.Property(share => share.Note).HasColumnName("note").HasMaxLength(NoteMaxLength);

            // Per-share settled (Layer A, §6). Payment metadata; never touches amount (settled-per-member OQ2).
            entity.Property(share => share.IsSettled)
                .HasColumnName("is_settled")
                .HasDefaultValue(false);

            entity.Property(share => share.SettledAt).HasColumnName("settled_at");

            entity.Property(share => share.CreatedAt).HasColumnName("created_at");
            entity.Property(share => share.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            // One share per member per expense (OQ5); the DB backstop for the in-transaction check.
            entity.HasIndex(share => new { share.ExpenseId, share.MemberId }).IsUnique();

            entity.HasOne(share => share.Expense)
                .WithMany(expense => expense.Shares)
                .HasForeignKey(share => share.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(share => share.Member)
                .WithMany()
                .HasForeignKey(share => share.MemberId)
                .OnDelete(DeleteBehavior.Restrict);
        });
}
