using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class EventShareLink
{
    /// <summary>Max length of the opaque token column (a 256-bit CSPRNG value Base64Url-encodes to 43 chars, OQ3b).</summary>
    public const int TokenMaxLength = 64;

    /// <summary>Max length of the snapshot BIN (NAPAS BIN is 6 digits; kept roomy).</summary>
    public const int BankBinMaxLength = 16;

    /// <summary>Max length of the snapshot bank name.</summary>
    public const int BankNameMaxLength = 100;

    /// <summary>Max length of the snapshot account number.</summary>
    public const int AccountNumberMaxLength = 32;

    /// <summary>Max length of the snapshot account-holder name.</summary>
    public const int AccountHolderNameMaxLength = 100;

    public EventShareLink()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EventShareLink>(entity =>
        {
            entity.ToTable("event_share_links");

            entity.HasKey(link => link.Id);
            entity.Property(link => link.Id).HasColumnName("id");

            entity.Property(link => link.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(link => link.Uuid).IsUnique();

            entity.Property(link => link.UserId).HasColumnName("user_id");
            entity.HasIndex(link => link.UserId);

            entity.Property(link => link.EventId).HasColumnName("event_id");
            entity.HasIndex(link => link.EventId);

            entity.Property(link => link.Token).HasColumnName("token").HasMaxLength(TokenMaxLength);
            entity.HasIndex(link => link.Token).IsUnique();

            entity.Property(link => link.ExpiresAt).HasColumnName("expires_at");
            entity.Property(link => link.RevokedAt).HasColumnName("revoked_at");

            entity.Property(link => link.BankAccountUuid).HasColumnName("bank_account_uuid").HasMaxLength(64);
            entity.Property(link => link.BankBin).HasColumnName("bank_bin").HasMaxLength(BankBinMaxLength);
            entity.Property(link => link.BankName).HasColumnName("bank_name").HasMaxLength(BankNameMaxLength);
            entity.Property(link => link.AccountNumber).HasColumnName("account_number").HasMaxLength(AccountNumberMaxLength);
            entity.Property(link => link.AccountHolderName)
                .HasColumnName("account_holder_name")
                .HasMaxLength(AccountHolderNameMaxLength);

            entity.Property(link => link.CreatedAt).HasColumnName("created_at");
            entity.Property(link => link.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(link => link.User)
                .WithMany()
                .HasForeignKey(link => link.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(link => link.Event)
                .WithMany()
                .HasForeignKey(link => link.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
