using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class BankAccount
{
    /// <summary>Max length of the NAPAS BIN (exactly 6 digits, OQ5).</summary>
    public const int BankBinMaxLength = 6;

    /// <summary>Max length of the display bank name (OQ5).</summary>
    public const int BankNameMaxLength = 100;

    /// <summary>Max length of the account number (numeric, up to 19 digits, OQ5).</summary>
    public const int AccountNumberMaxLength = 19;

    /// <summary>Max length of the account-holder name (OQ5).</summary>
    public const int AccountHolderNameMaxLength = 100;

    public BankAccount()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.ToTable("bank_accounts");

            entity.HasKey(account => account.Id);
            entity.Property(account => account.Id).HasColumnName("id");

            entity.Property(account => account.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(account => account.Uuid).IsUnique();

            entity.Property(account => account.UserId).HasColumnName("user_id");
            entity.HasIndex(account => account.UserId);

            entity.Property(account => account.BankBin).HasColumnName("bank_bin").HasMaxLength(BankBinMaxLength);

            entity.Property(account => account.BankName).HasColumnName("bank_name").HasMaxLength(BankNameMaxLength);

            entity.Property(account => account.AccountNumber).HasColumnName("account_number").HasMaxLength(AccountNumberMaxLength);

            entity.Property(account => account.AccountHolderName)
                .HasColumnName("account_holder_name")
                .HasMaxLength(AccountHolderNameMaxLength);

            entity.Property(account => account.IsDefault)
                .HasColumnName("is_default")
                .HasDefaultValue(false);

            entity.Property(account => account.CreatedAt).HasColumnName("created_at");
            entity.Property(account => account.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(account => account.User)
                .WithMany()
                .HasForeignKey(account => account.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
