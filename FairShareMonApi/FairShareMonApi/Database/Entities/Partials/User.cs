using FairShareMonApi.Constants;
using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class User
{
    public User()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
        Tier = UserTiers.Free;
        Role = UserRoles.User;
        Status = UserStatuses.Active;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id");

            entity.Property(user => user.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(user => user.Uuid).IsUnique();

            entity.Property(user => user.Username).HasColumnName("username").HasMaxLength(32);
            entity.HasIndex(user => user.Username).IsUnique();

            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(100);

            entity.Property(user => user.Tier).HasColumnName("tier").HasMaxLength(16).HasDefaultValue(UserTiers.Free);

            entity.Property(user => user.Role).HasColumnName("role").HasMaxLength(16).HasDefaultValue(UserRoles.User);
            entity.HasIndex(user => user.Role);

            entity.Property(user => user.Status).HasColumnName("status").HasMaxLength(16).HasDefaultValue(UserStatuses.Active);
            entity.HasIndex(user => user.Status);

            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");
        });
}
