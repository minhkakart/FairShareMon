using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class ExpenseTag
{
    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ExpenseTag>(entity =>
        {
            entity.ToTable("expense_tags");

            // Composite PK - a pure link table needs no surrogate key/uuid (OQ6).
            entity.HasKey(expenseTag => new { expenseTag.ExpenseId, expenseTag.TagId });

            entity.Property(expenseTag => expenseTag.ExpenseId).HasColumnName("expense_id");
            entity.Property(expenseTag => expenseTag.TagId).HasColumnName("tag_id");

            // Serves the tag filter on the expense list (OQ13).
            entity.HasIndex(expenseTag => expenseTag.TagId);

            entity.HasOne(expenseTag => expenseTag.Expense)
                .WithMany(expense => expense.ExpenseTags)
                .HasForeignKey(expenseTag => expenseTag.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(expenseTag => expenseTag.Tag)
                .WithMany()
                .HasForeignKey(expenseTag => expenseTag.TagId)
                .OnDelete(DeleteBehavior.Restrict);
        });
}
