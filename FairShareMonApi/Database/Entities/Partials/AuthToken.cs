using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class AuthToken
{
    public AuthToken()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<AuthToken>(entity =>
        {
            entity.ToTable("auth_tokens");

            entity.HasKey(token => token.Id);
            entity.Property(token => token.Id).HasColumnName("id");

            entity.Property(token => token.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(token => token.Uuid).IsUnique();

            entity.Property(token => token.UserId).HasColumnName("user_id");
            entity.HasIndex(token => token.UserId);

            entity.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength();
            entity.HasIndex(token => token.TokenHash).IsUnique();

            entity.Property(token => token.TokenType).HasColumnName("token_type").HasMaxLength(16);

            entity.Property(token => token.PairUuid).HasColumnName("pair_uuid").HasMaxLength(64);
            entity.HasIndex(token => token.PairUuid);

            entity.Property(token => token.ExpiresAt).HasColumnName("expires_at");
            entity.Property(token => token.RevokedAt).HasColumnName("revoked_at");

            entity.Property(token => token.CreatedAt).HasColumnName("created_at");
            entity.Property(token => token.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
