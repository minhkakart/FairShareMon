using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class Expense
{
    /// <summary>Max length of an expense name (OQ16).</summary>
    public const int NameMaxLength = 200;

    /// <summary>Max length of the optional description (OQ16).</summary>
    public const int DescriptionMaxLength = 1000;

    public Expense()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Expense>(entity =>
        {
            entity.ToTable("expenses");

            entity.HasKey(expense => expense.Id);
            entity.Property(expense => expense.Id).HasColumnName("id");

            entity.Property(expense => expense.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(expense => expense.Uuid).IsUnique();

            entity.Property(expense => expense.UserId).HasColumnName("user_id");
            entity.HasIndex(expense => expense.UserId);

            entity.Property(expense => expense.Name).HasColumnName("name").HasMaxLength(NameMaxLength);

            entity.Property(expense => expense.Description).HasColumnName("description").HasMaxLength(DescriptionMaxLength);

            entity.Property(expense => expense.ExpenseTime).HasColumnName("expense_time");

            entity.Property(expense => expense.PayerMemberId).HasColumnName("payer_member_id");

            entity.Property(expense => expense.CategoryId).HasColumnName("category_id");

            entity.Property(expense => expense.EventId).HasColumnName("event_id");
            entity.HasIndex(expense => expense.EventId);

            entity.Property(expense => expense.IsSettled)
                .HasColumnName("is_settled")
                .HasDefaultValue(false);

            entity.Property(expense => expense.SettledAt).HasColumnName("settled_at");

            entity.Property(expense => expense.CreatedAt).HasColumnName("created_at");
            entity.Property(expense => expense.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            // Composite index serving the default sort (expense_time DESC) + the range filter (OQ13).
            entity.HasIndex(expense => new { expense.UserId, expense.ExpenseTime });

            entity.HasOne(expense => expense.User)
                .WithMany()
                .HasForeignKey(expense => expense.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Payer/category are required links to soft-deleted-only tables, so Restrict is inert in
            // practice but kept for referential integrity (OQ7).
            entity.HasOne(expense => expense.PayerMember)
                .WithMany()
                .HasForeignKey(expense => expense.PayerMemberId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(expense => expense.Category)
                .WithMany()
                .HasForeignKey(expense => expense.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Deleting an event nulls its expenses' event_id (they go loose), never cascade-deletes (M6, OQ2).
            entity.HasOne(expense => expense.Event)
                .WithMany(evt => evt.Expenses)
                .HasForeignKey(expense => expense.EventId)
                .OnDelete(DeleteBehavior.SetNull);
        });
}
