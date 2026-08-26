using FairShareMonApi.Utils;
using Microsoft.EntityFrameworkCore;

namespace FairShareMonApi.Database.Entities;

public partial class BankTransactionCallback
{
    public const int ProviderKeyMaxLength = 32;
    public const int ProviderTransactionIdMaxLength = 128;
    public const int BankBinMaxLength = 16;
    public const int AccountNumberMaxLength = 32;
    public const int ContentMaxLength = 500;
    public const int ExtractedCodeMaxLength = 16;
    public const int FailureNoteMaxLength = 500;

    /// <summary>Name of the composite unique index enforcing the idempotency dedup key.</summary>
    public const string ProviderTransactionUniqueIndexName = "ux_bank_transaction_callbacks_provider_tx";

    public BankTransactionCallback()
    {
        Uuid = Utils.Uuid.NewV7();
        CreatedAt = AppDateTime.Now;
    }

    public static void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<BankTransactionCallback>(entity =>
        {
            entity.ToTable("bank_transaction_callbacks", table => table.HasCheckConstraint(
                "ck_bank_transaction_callbacks_amount_non_negative", "amount >= 0"));

            entity.HasKey(callback => callback.Id);
            entity.Property(callback => callback.Id).HasColumnName("id");

            entity.Property(callback => callback.Uuid).HasColumnName("uuid").HasMaxLength(64);
            entity.HasIndex(callback => callback.Uuid).IsUnique();

            entity.Property(callback => callback.ProviderKey).HasColumnName("provider_key").HasMaxLength(ProviderKeyMaxLength);
            entity.Property(callback => callback.ProviderTransactionId)
                .HasColumnName("provider_transaction_id")
                .HasMaxLength(ProviderTransactionIdMaxLength);

            // The idempotency dedup key (Requirements): a webhook retried/duplicated by the provider
            // must not double-apply.
            entity.HasIndex(callback => new { callback.ProviderKey, callback.ProviderTransactionId })
                .IsUnique()
                .HasDatabaseName(ProviderTransactionUniqueIndexName);

            entity.Property(callback => callback.IsIncoming).HasColumnName("is_incoming");

            entity.Property(callback => callback.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");

            entity.Property(callback => callback.BankBin).HasColumnName("bank_bin").HasMaxLength(BankBinMaxLength);
            entity.Property(callback => callback.DestinationAccountNumber)
                .HasColumnName("destination_account_number")
                .HasMaxLength(AccountNumberMaxLength);

            entity.Property(callback => callback.Content).HasColumnName("content").HasMaxLength(ContentMaxLength);

            entity.Property(callback => callback.ExtractedCode).HasColumnName("extracted_code").HasMaxLength(ExtractedCodeMaxLength);
            // Supports an "all callbacks for this code" debug query.
            entity.HasIndex(callback => callback.ExtractedCode);

            entity.Property(callback => callback.TransactionAt).HasColumnName("transaction_at");

            entity.Property(callback => callback.RawPayload).HasColumnName("raw_payload").HasColumnType("longtext");

            entity.Property(callback => callback.MatchedCorrelationCodeId).HasColumnName("matched_correlation_code_id");

            entity.Property(callback => callback.ResolvedUserId).HasColumnName("resolved_user_id");
            // The list-endpoint scope column (GET api/v1/bank-callbacks).
            entity.HasIndex(callback => callback.ResolvedUserId);

            entity.Property(callback => callback.Outcome).HasColumnName("outcome").HasConversion<int>();

            entity.Property(callback => callback.FailureNote).HasColumnName("failure_note").HasMaxLength(FailureNoteMaxLength);

            entity.Property(callback => callback.AppliedAt).HasColumnName("applied_at");

            entity.Property(callback => callback.CreatedAt).HasColumnName("created_at");
            entity.Property(callback => callback.UpdatedAt)
                .HasColumnName("updated_at")
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("current_timestamp(6) ON UPDATE current_timestamp(6)");

            entity.HasOne(callback => callback.MatchedCorrelationCode)
                .WithMany()
                .HasForeignKey(callback => callback.MatchedCorrelationCodeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(callback => callback.ResolvedUser)
                .WithMany()
                .HasForeignKey(callback => callback.ResolvedUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
}
