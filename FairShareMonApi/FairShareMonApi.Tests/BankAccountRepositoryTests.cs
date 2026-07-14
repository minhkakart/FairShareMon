using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for <c>BankAccountRepository</c> against the real MariaDB (skippable). Covers entity
/// defaults (uuid/UTC/user_id), the single-default invariant (first account auto-default; atomic
/// set-default swap keeping exactly one default; delete-of-default promotes the most-recent remaining;
/// delete-last empties the wallet; non-default delete leaves the default intact), resource-owned scoping
/// (another user's account invisible on get/list/update/delete/set-default), hard-delete removes the row
/// (no is_deleted, OQ7), update never touches is_default (OQ6), and no uniqueness constraint (OQ16).
///
/// Cleanup: <see cref="DisposeAsync"/> hard-deletes the prefix's bank_accounts first (defensive - the
/// user FK cascades on user delete anyway), then the base class deletes the prefix's users.
/// </summary>
[Collection("AuthIntegration")]
public class BankAccountRepositoryTests(DatabaseFixture fixture) : AuthDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private BankAccountRepository CreateRepository() => new(CreateContext());

    private async Task<BankAccount> CreateAsync(User user, string bankName = "Vietcombank", string bin = "970436", string number = "0123456789") =>
        (await CreateRepository().CreateAsync(user.Uuid, bin, bankName, number, "Nguyen Van A"))!;

    private async Task<BankAccount> ReloadAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.BankAccounts.AsNoTracking().SingleAsync(account => account.Uuid == uuid);
    }

    private async Task<BankAccount?> FindAsync(string uuid)
    {
        await using var context = CreateContext();
        return await context.BankAccounts.AsNoTracking().FirstOrDefaultAsync(account => account.Uuid == uuid);
    }

    private async Task<int> CountForUserAsync(ulong userId)
    {
        await using var context = CreateContext();
        return await context.BankAccounts.CountAsync(account => account.UserId == userId);
    }

    [SkippableFact]
    public async Task CreateAsync_NewAccount_PersistsWithUuidUtcUserId()
    {
        var user = await SeedUserAsync();
        var before = DateTime.UtcNow.AddSeconds(-1);

        var created = await CreateAsync(user, bankName: "Vietcombank");
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.NotNull(created);
        Assert.Equal(36, created.Uuid.Length);
        Assert.InRange(created.CreatedAt, before, after); // AppDateTime.Now = UTC

        var persisted = await ReloadAsync(created.Uuid);
        Assert.Equal(user.Id, persisted.UserId);
        Assert.Equal("Vietcombank", persisted.BankName);
        Assert.Equal("970436", persisted.BankBin);
        Assert.Equal("0123456789", persisted.AccountNumber);
        Assert.Equal("Nguyen Van A", persisted.AccountHolderName);
    }

    [SkippableFact]
    public async Task CreateAsync_FirstAccount_IsAutoDefault()
    {
        var user = await SeedUserAsync();

        var first = await CreateAsync(user);

        Assert.True((await ReloadAsync(first.Uuid)).IsDefault); // OQ6: first account auto-default
    }

    [SkippableFact]
    public async Task CreateAsync_SecondAccount_IsNotDefault()
    {
        var user = await SeedUserAsync();
        await CreateAsync(user, bankName: "Vietcombank");

        var second = await CreateAsync(user, bankName: "Techcombank");

        Assert.False((await ReloadAsync(second.Uuid)).IsDefault);
        // Exactly one default remains.
        Assert.Equal(1, await CountDefaultsAsync(user.Id));
    }

    [SkippableFact]
    public async Task CreateAsync_UnknownUser_ReturnsNull()
    {
        var result = await CreateRepository().CreateAsync("00000000-0000-7000-8000-000000000000", "970436", "Vietcombank", "0123456789", "Nguyen Van A");

        Assert.Null(result);
    }

    [SkippableFact]
    public async Task CreateAsync_DuplicateBankAndAccount_IsAllowed()
    {
        var user = await SeedUserAsync();
        await CreateAsync(user, bin: "970436", number: "0123456789");

        var duplicate = await CreateAsync(user, bin: "970436", number: "0123456789"); // OQ16: no uniqueness

        Assert.NotNull(duplicate);
        Assert.Equal(2, await CountForUserAsync(user.Id));
    }

    [SkippableFact]
    public async Task GetByUuidAsync_AnotherUsersAccount_ReturnsNull()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var account = await CreateAsync(owner);

        var seenByStranger = await CreateRepository().GetByUuidAsync(stranger.Uuid, account.Uuid);
        var seenByOwner = await CreateRepository().GetByUuidAsync(owner.Uuid, account.Uuid);

        Assert.Null(seenByStranger); // resource-owned: existence not leaked
        Assert.NotNull(seenByOwner);
    }

    [SkippableFact]
    public async Task GetDefaultAsync_ReturnsTheDefault_NullWhenEmpty()
    {
        var user = await SeedUserAsync();
        Assert.Null(await CreateRepository().GetDefaultAsync(user.Uuid)); // empty wallet

        var first = await CreateAsync(user);
        var found = await CreateRepository().GetDefaultAsync(user.Uuid);

        Assert.NotNull(found);
        Assert.Equal(first.Uuid, found!.Uuid);
    }

    [SkippableFact]
    public async Task ListByUserAsync_ReturnsOnlyCallersAccounts_SortedDefaultFirst()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        await CreateAsync(owner, bankName: "Vietcombank"); // default (first)
        await CreateAsync(owner, bankName: "Techcombank");
        await CreateAsync(stranger, bankName: "Stranger");

        var list = await CreateRepository().ListByUserAsync(owner.Uuid);

        Assert.Equal(2, list.Count);
        Assert.True(list[0].IsDefault);                 // default first
        Assert.Equal("Vietcombank", list[0].BankName);
        Assert.DoesNotContain(list, account => account.BankName == "Stranger");
    }

    [SkippableFact]
    public async Task UpdateAsync_OwnedAccount_PersistsFieldsButNeverTouchesDefault()
    {
        var user = await SeedUserAsync();
        var account = await CreateAsync(user); // auto-default

        var updated = await CreateRepository().UpdateAsync(user.Uuid, account.Uuid, "970422", "MB Bank", "9998887776", "Tran Thi B");

        Assert.True(updated);
        var persisted = await ReloadAsync(account.Uuid);
        Assert.Equal("970422", persisted.BankBin);
        Assert.Equal("MB Bank", persisted.BankName);
        Assert.Equal("9998887776", persisted.AccountNumber);
        Assert.Equal("Tran Thi B", persisted.AccountHolderName);
        Assert.True(persisted.IsDefault); // OQ6: update leaves is_default alone
    }

    [SkippableFact]
    public async Task UpdateAsync_AnotherUsersAccount_ReturnsFalseAndDoesNotChangeIt()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var account = await CreateAsync(owner, bankName: "Vietcombank");

        var updated = await CreateRepository().UpdateAsync(stranger.Uuid, account.Uuid, "970422", "Hacked", "9998887776", "Hacker");

        Assert.False(updated);
        Assert.Equal("Vietcombank", (await ReloadAsync(account.Uuid)).BankName);
    }

    [SkippableFact]
    public async Task SetDefaultAsync_ClearsPreviousDefaultAndSetsTargetAtomically()
    {
        var user = await SeedUserAsync();
        var oldDefault = await CreateAsync(user, bankName: "Vietcombank"); // auto-default
        var target = await CreateAsync(user, bankName: "Techcombank");

        var result = await CreateRepository().SetDefaultAsync(user.Uuid, target.Uuid);

        Assert.True(result);
        Assert.False((await ReloadAsync(oldDefault.Uuid)).IsDefault); // exactly the old one cleared
        Assert.True((await ReloadAsync(target.Uuid)).IsDefault);
        Assert.Equal(1, await CountDefaultsAsync(user.Id)); // never zero, never two
    }

    [SkippableFact]
    public async Task SetDefaultAsync_AlreadyDefault_StaysExactlyOneDefault()
    {
        var user = await SeedUserAsync();
        var account = await CreateAsync(user); // auto-default

        var result = await CreateRepository().SetDefaultAsync(user.Uuid, account.Uuid);

        Assert.True(result);
        Assert.True((await ReloadAsync(account.Uuid)).IsDefault);
        Assert.Equal(1, await CountDefaultsAsync(user.Id));
    }

    [SkippableFact]
    public async Task SetDefaultAsync_AnotherUsersAccount_ReturnsFalse()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var account = await CreateAsync(owner);

        var result = await CreateRepository().SetDefaultAsync(stranger.Uuid, account.Uuid);

        Assert.False(result); // resource-owned: a foreign account is invisible
    }

    [SkippableFact]
    public async Task DeleteAsync_Default_PromotesMostRecentRemaining()
    {
        var user = await SeedUserAsync();
        var first = await CreateAsync(user, bankName: "Vietcombank"); // auto-default
        await CreateAsync(user, bankName: "Techcombank");
        var newest = await CreateAsync(user, bankName: "MB Bank");

        var deleted = await CreateRepository().DeleteAsync(user.Uuid, first.Uuid);

        Assert.True(deleted);
        Assert.Null(await FindAsync(first.Uuid));                 // gone
        Assert.True((await ReloadAsync(newest.Uuid)).IsDefault);  // most-recent remaining promoted (OQ6)
        Assert.Equal(1, await CountDefaultsAsync(user.Id));
    }

    [SkippableFact]
    public async Task DeleteAsync_NonDefault_LeavesDefaultIntact()
    {
        var user = await SeedUserAsync();
        var theDefault = await CreateAsync(user, bankName: "Vietcombank"); // auto-default
        var other = await CreateAsync(user, bankName: "Techcombank");

        var deleted = await CreateRepository().DeleteAsync(user.Uuid, other.Uuid);

        Assert.True(deleted);
        Assert.True((await ReloadAsync(theDefault.Uuid)).IsDefault); // untouched
        Assert.Equal(1, await CountDefaultsAsync(user.Id));
    }

    [SkippableFact]
    public async Task DeleteAsync_LastAccount_LeavesWalletEmptyWithNoDefault()
    {
        var user = await SeedUserAsync();
        var only = await CreateAsync(user);

        var deleted = await CreateRepository().DeleteAsync(user.Uuid, only.Uuid);

        Assert.True(deleted);
        Assert.Equal(0, await CountForUserAsync(user.Id));            // empty wallet is valid (OQ6)
        Assert.Null(await CreateRepository().GetDefaultAsync(user.Uuid));
    }

    [SkippableFact]
    public async Task DeleteAsync_HardDeletes_RemovesTheRowEntirely()
    {
        var user = await SeedUserAsync();
        var account = await CreateAsync(user);

        await CreateRepository().DeleteAsync(user.Uuid, account.Uuid);

        Assert.Null(await FindAsync(account.Uuid)); // OQ7: hard-delete, no is_deleted flag
    }

    [SkippableFact]
    public async Task DeleteAsync_AnotherUsersAccount_ReturnsFalseAndKeepsTheRow()
    {
        var owner = await SeedUserAsync();
        var stranger = await SeedUserAsync();
        var account = await CreateAsync(owner);

        var deleted = await CreateRepository().DeleteAsync(stranger.Uuid, account.Uuid);

        Assert.False(deleted);
        Assert.NotNull(await FindAsync(account.Uuid));
    }

    private async Task<int> CountDefaultsAsync(ulong userId)
    {
        await using var context = CreateContext();
        return await context.BankAccounts.CountAsync(account => account.UserId == userId && account.IsDefault);
    }

    public override async Task DisposeAsync()
    {
        if (Fixture.IsAvailable)
        {
            await using var context = CreateContext();
            var userIds = await context.Users
                .Where(user => user.Username.StartsWith(UsernamePrefix))
                .Select(user => user.Id)
                .ToListAsync();
            await context.BankAccounts.Where(account => userIds.Contains(account.UserId)).ExecuteDeleteAsync();
        }

        await base.DisposeAsync();
    }
}
