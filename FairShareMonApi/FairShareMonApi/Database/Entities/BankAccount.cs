using FairShareMonApi.Database.Abstractions;

namespace FairShareMonApi.Database.Entities;

/// <summary>
/// A receiving bank account in the owner's wallet (ví, table <c>bank_accounts</c>, The-ideal.md
/// §3.10). Belongs to exactly one <see cref="User"/> (<c>user_id</c>). Carries what VietQR needs to
/// name the transfer destination: the NAPAS acquirer BIN, a display bank name, the account number and
/// the account-holder name, plus a single-default flag (<see cref="IsDefault"/>) used as the QR
/// receive destination. Exactly one default exists whenever the wallet is non-empty (atomic swap via
/// <c>PUT /{uuid}/default</c>, mirroring the default-category invariant); a wallet may legitimately be
/// empty. <b>Hard-deleted</b> (NOT <see cref="IEntityDeletable"/>): a bank account has no historical
/// ledger linkage - QR images are generated on demand and never persisted, so a deleted account leaves
/// no dangling history (OQ7).
/// </summary>
public partial class BankAccount : IEntity
{
    public ulong Id { get; set; }

    public string Uuid { get; set; }

    /// <summary>Owning user (FK -&gt; <c>users.id</c>, cascade delete).</summary>
    public ulong UserId { get; set; }

    /// <summary>NAPAS acquirer bank identifier (BIN), exactly 6 digits.</summary>
    public required string BankBin { get; set; }

    /// <summary>Display bank name (client-provided, max 100). The server does not curate a banks table.</summary>
    public required string BankName { get; set; }

    /// <summary>Receiving account number (digits only, 6-19 chars).</summary>
    public required string AccountNumber { get; set; }

    /// <summary>Account-holder name (max 100). Used as the composite label and shown by some bank apps.</summary>
    public required string AccountHolderName { get; set; }

    /// <summary>True for the single default account: the QR receive destination unless another is chosen at generation time.</summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
