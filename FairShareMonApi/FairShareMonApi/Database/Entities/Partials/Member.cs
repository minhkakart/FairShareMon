using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class Member
{
    /// <summary>Fixed default name of the owner-representative member at registration (OQ5). Renamable afterwards.</summary>
    public const string OwnerRepresentativeDefaultName = "Tôi";

    public Member()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Member>(entity =>
        {
            entity.ToTable("members");

            entity.HasKey(member => member.Id);
            entity.Property(member => member.Id).HasColumnName("id");

            entity.Property(member => member.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(member => member.Uuid).IsUnique();

            entity.Property(member => member.UserId).HasColumnName("user_id");
            entity.HasIndex(member => member.UserId);

            entity.Property(member => member.Name).HasColumnName("name").HasMaxLength(100);

            entity.Property(member => member.IsOwnerRepresentative)
                .HasColumnName("is_owner_representative")
                .HasDefaultValue(false);

            entity.Property(member => member.IsDeleted)
                .HasColumnName("is_deleted")
                .HasDefaultValue(false);

            entity.Property(member => member.CreatedAt).HasColumnName("created_at");
            entity.Property(member => member.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(member => member.User)
                .WithMany()
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
