using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class Category
{
    /// <summary>Max length of a category name (mirrors Members, OQ6).</summary>
    public const int NameMaxLength = 100;

    /// <summary>Max length of the <c>#RRGGBB</c> color string (OQ2).</summary>
    public const int ColorMaxLength = 7;

    /// <summary>Max length of the optional icon key (OQ2).</summary>
    public const int IconMaxLength = 50;

    /// <summary>
    /// The suggested-category set seeded on registration (OQ1). Defined once so the bootstrap step
    /// and the backfill stay in sync. Exactly one entry is the default ("Ăn uống").
    /// </summary>
    public static readonly IReadOnlyList<SuggestedCategory> SuggestedCategories =
    [
        new("Ăn uống", "🍜", "#F97316", IsDefault: true),
        new("Đi lại", "🚗", "#3B82F6", IsDefault: false),
        new("Khách sạn", "🏨", "#8B5CF6", IsDefault: false),
        new("Mua sắm", "🛍️", "#EC4899", IsDefault: false),
        new("Khác", "⋯", "#6B7280", IsDefault: false)
    ];

    public Category()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    /// <summary>
    /// Builds the suggested-category rows (OQ1) for a user, defined once here so the registration
    /// bootstrap step and the idempotent backfill stay identical. Exactly one row is the default.
    /// </summary>
    public static IReadOnlyList<Category> BuildSuggestedSet(ulong userId) =>
        SuggestedCategories
            .Select(suggested => new Category
            {
                UserId = userId,
                Name = suggested.Name,
                Color = suggested.Color,
                Icon = suggested.Icon,
                IsDefault = suggested.IsDefault
            })
            .ToList();

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("categories");

            entity.HasKey(category => category.Id);
            entity.Property(category => category.Id).HasColumnName("id");

            entity.Property(category => category.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(category => category.Uuid).IsUnique();

            entity.Property(category => category.UserId).HasColumnName("user_id");
            entity.HasIndex(category => category.UserId);

            entity.Property(category => category.Name).HasColumnName("name").HasMaxLength(NameMaxLength);

            entity.Property(category => category.Color).HasColumnName("color").HasMaxLength(ColorMaxLength);

            entity.Property(category => category.Icon).HasColumnName("icon").HasMaxLength(IconMaxLength);

            entity.Property(category => category.IsDefault)
                .HasColumnName("is_default")
                .HasDefaultValue(false);

            entity.Property(category => category.IsDeleted)
                .HasColumnName("is_deleted")
                .HasDefaultValue(false);

            entity.Property(category => category.CreatedAt).HasColumnName("created_at");
            entity.Property(category => category.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(category => category.User)
                .WithMany()
                .HasForeignKey(category => category.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}

/// <summary>A suggested-category seed descriptor (name, icon, color, default flag) - OQ1.</summary>
public sealed record SuggestedCategory(string Name, string Icon, string Color, bool IsDefault);
