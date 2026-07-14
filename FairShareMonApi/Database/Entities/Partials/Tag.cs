using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class Tag
{
    /// <summary>Max length of a tag name (mirrors Members/Categories, OQ6).</summary>
    public const int NameMaxLength = 100;

    public Tag()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("tags");

            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.Id).HasColumnName("id");

            entity.Property(tag => tag.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(tag => tag.Uuid).IsUnique();

            entity.Property(tag => tag.UserId).HasColumnName("user_id");
            entity.HasIndex(tag => tag.UserId);

            entity.Property(tag => tag.Name).HasColumnName("name").HasMaxLength(NameMaxLength);

            entity.Property(tag => tag.IsDeleted)
                .HasColumnName("is_deleted")
                .HasDefaultValue(false);

            entity.Property(tag => tag.CreatedAt).HasColumnName("created_at");
            entity.Property(tag => tag.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(tag => tag.User)
                .WithMany()
                .HasForeignKey(tag => tag.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
