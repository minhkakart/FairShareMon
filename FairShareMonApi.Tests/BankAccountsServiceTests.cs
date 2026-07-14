using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Validators.Wallet;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>BankAccountsService</c> over a fake <see cref="IBankAccountRepository"/>, the
/// real AutoMapper profile and real validators (no DB). Proves: get/update/set-default/delete on an
/// ownership miss -> <c>BankAccountNotFound</c> 12000; create trims + maps to the response; update never
/// touches <c>is_default</c>; invalid input -> a validation exception before the repository is hit.
/// </summary>
public class BankAccountsServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000ba01";

    private readonly FakeBankAccountRepository _repository = new();
    // Pass-through tier double: these tests target mapping/trim/miss behaviour, not the Premium gate
    // (which is proved at the service-throws and endpoint levels), so the gate stays a no-op here.
    private readonly FakeTierService _tier = new();
    private readonly IMapper _mapper = new MapperConfiguration(config => config.AddProfile<BankAccountProfile>()).CreateMapper();

    private BankAccountsService CreateService() =>
        new(_repository, _tier, _mapper, new CreateBankAccountRequestValidator(), new UpdateBankAccountRequestValidator());

    private BankAccount Add(string bankName = "Vietcombank", bool isDefault = false)
    {
        var account = new BankAccount
        {
            BankBin = "970436",
            BankName = bankName,
            AccountNumber = "0123456789",
            AccountHolderName = "Nguyen Van A",
            IsDefault = isDefault
        };
        _repository.Accounts.Add((UserUuid, account));
        return account;
    }

    private static CreateBankAccountRequest CreateRequest(
        string bankBin = "970436", string bankName = "Vietcombank",
        string accountNumber = "0123456789", string accountHolderName = "Nguyen Van A") =>
        new() { BankBin = bankBin, BankName = bankName, AccountNumber = accountNumber, AccountHolderName = accountHolderName };

    [Fact]
    public async Task CreateAsync_ValidRequest_TrimsAndMapsToResponse()
    {
        var response = await CreateService().CreateAsync(UserUuid, CreateRequest(
            bankName: "  Vietcombank  ", accountNumber: "0123456789", accountHolderName: "  Nguyen Van A  "));

        Assert.Equal("970436", response.BankBin);
        Assert.Equal("Vietcombank", response.BankName);          // trimmed
        Assert.Equal("Nguyen Van A", response.AccountHolderName); // trimmed
        Assert.True(response.IsDefault);                          // first account auto-default (fake mirrors OQ6)
        Assert.Single(_repository.Accounts);
    }

    [Fact]
    public async Task CreateAsync_SecondAccount_IsNotDefault()
    {
        Add(isDefault: true); // existing default
        var response = await CreateService().CreateAsync(UserUuid, CreateRequest(bankName: "Techcombank"));

        Assert.False(response.IsDefault);
    }

    [Fact]
    public async Task CreateAsync_InvalidBankBin_ThrowsValidationExceptionBeforeRepository()
    {
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().CreateAsync(UserUuid, CreateRequest(bankBin: "12")));

        Assert.Empty(_repository.Accounts); // never reached the repository
    }

    [Fact]
    public async Task CreateAsync_UnknownUser_ThrowsBankAccountNotFound12000()
    {
        _repository.FailCreateWithUnknownUser = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, CreateRequest()));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    // ---- M10 Premium feature-gate (OQ5b read-vs-mutation split) -----------------------------------

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("setdefault")]
    [InlineData("delete")]
    public async Task Mutations_FreeCaller_ThrowPremiumFeatureRequired13003BeforeTouchingData(string operation)
    {
        var account = Add(isDefault: true);
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired; // simulate a Free caller hitting the gate

        var service = CreateService();
        var exception = await Assert.ThrowsAsync<ErrorException>(() => operation switch
        {
            "create" => service.CreateAsync(UserUuid, CreateRequest()),
            "update" => service.UpdateAsync(UserUuid, account.Uuid, new UpdateBankAccountRequest
            {
                BankBin = "970436", BankName = "X", AccountNumber = "0123456789", AccountHolderName = "Y"
            }),
            "setdefault" => service.SetDefaultAsync(UserUuid, account.Uuid),
            _ => service.DeleteAsync(UserUuid, account.Uuid)
        });

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    [Fact]
    public async Task Reads_FreeCaller_StayOpenEvenWhenGateWouldThrow()
    {
        var account = Add(isDefault: true);
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired; // gate would fire on a mutation, but reads bypass it

        var list = await CreateService().ListAsync(UserUuid);
        var single = await CreateService().GetAsync(UserUuid, account.Uuid);

        Assert.Single(list);                 // list read is not gated (OQ5b)
        Assert.Equal(account.Uuid, single.Uuid); // get read is not gated (OQ5b)
    }

    [Fact]
    public async Task GetAsync_Miss_ThrowsBankAccountNotFound12000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetAsync(UserUuid, "no-such-account"));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Found_ReturnsResponse()
    {
        var account = Add(isDefault: true);

        var response = await CreateService().GetAsync(UserUuid, account.Uuid);

        Assert.Equal(account.Uuid, response.Uuid);
        Assert.True(response.IsDefault);
    }

    [Fact]
    public async Task ListAsync_MapsAllOwnedAccounts()
    {
        Add(bankName: "Vietcombank", isDefault: true);
        Add(bankName: "Techcombank");

        var list = await CreateService().ListAsync(UserUuid);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task UpdateAsync_Miss_ThrowsBankAccountNotFound12000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "no-such-account", new UpdateBankAccountRequest
            {
                BankBin = "970436", BankName = "X", AccountNumber = "0123456789", AccountHolderName = "Y"
            }));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task UpdateAsync_OwnedAccount_PersistsFieldsButNeverTouchesDefault()
    {
        var account = Add(bankName: "Vietcombank", isDefault: true);

        var response = await CreateService().UpdateAsync(UserUuid, account.Uuid, new UpdateBankAccountRequest
        {
            BankBin = "970422",
            BankName = "  MB Bank  ",
            AccountNumber = "9998887776",
            AccountHolderName = "  Tran Thi B  "
        });

        Assert.Equal("970422", response.BankBin);
        Assert.Equal("MB Bank", response.BankName);       // trimmed
        Assert.Equal("Tran Thi B", response.AccountHolderName);
        Assert.True(response.IsDefault);                  // default flag preserved (OQ6)
        Assert.True(account.IsDefault);                   // the stored row's flag is untouched
    }

    [Fact]
    public async Task SetDefaultAsync_Miss_ThrowsBankAccountNotFound12000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().SetDefaultAsync(UserUuid, "no-such-account"));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task SetDefaultAsync_OwnedAccount_Succeeds()
    {
        var current = Add(bankName: "Vietcombank", isDefault: true);
        var target = Add(bankName: "Techcombank");

        await CreateService().SetDefaultAsync(UserUuid, target.Uuid);

        Assert.True(target.IsDefault);
        Assert.False(current.IsDefault);
    }

    [Fact]
    public async Task DeleteAsync_Miss_ThrowsBankAccountNotFound12000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DeleteAsync(UserUuid, "no-such-account"));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_OwnedAccount_Removes()
    {
        var account = Add();

        await CreateService().DeleteAsync(UserUuid, account.Uuid);

        Assert.Empty(_repository.Accounts);
    }

    /// <summary>
    /// In-memory stand-in for the bank_accounts table. Mirrors the single-default invariant (first
    /// account auto-default; atomic swap on set-default; promote-another on delete-of-default) so the
    /// service's mapping/trim/miss behaviour is provable without a DB.
    /// </summary>
    private sealed class FakeBankAccountRepository : IBankAccountRepository
    {
        public List<(string UserUuid, BankAccount Account)> Accounts { get; } = [];

        public bool FailCreateWithUnknownUser { get; set; }

        public Task<IReadOnlyList<BankAccount>> ListByUserAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BankAccount>>(Accounts
                .Where(entry => entry.UserUuid == userUuid)
                .OrderByDescending(entry => entry.Account.IsDefault)
                .Select(entry => entry.Account)
                .ToList());

        public Task<BankAccount?> GetByUuidAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts
                .Where(entry => entry.UserUuid == userUuid && entry.Account.Uuid == bankAccountUuid)
                .Select(entry => entry.Account)
                .FirstOrDefault());

        public Task<BankAccount?> GetDefaultAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts
                .Where(entry => entry.UserUuid == userUuid && entry.Account.IsDefault)
                .Select(entry => entry.Account)
                .FirstOrDefault());

        public Task<BankAccount?> CreateAsync(string userUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default)
        {
            if (FailCreateWithUnknownUser)
                return Task.FromResult<BankAccount?>(null);

            var isFirst = !Accounts.Any(entry => entry.UserUuid == userUuid);
            var account = new BankAccount
            {
                BankBin = bankBin,
                BankName = bankName,
                AccountNumber = accountNumber,
                AccountHolderName = accountHolderName,
                IsDefault = isFirst
            };
            Accounts.Add((userUuid, account));
            return Task.FromResult<BankAccount?>(account);
        }

        public Task<bool> UpdateAsync(string userUuid, string bankAccountUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default)
        {
            var account = Accounts
                .Where(entry => entry.UserUuid == userUuid && entry.Account.Uuid == bankAccountUuid)
                .Select(entry => entry.Account)
                .FirstOrDefault();
            if (account is null)
                return Task.FromResult(false);

            account.BankBin = bankBin;
            account.BankName = bankName;
            account.AccountNumber = accountNumber;
            account.AccountHolderName = accountHolderName;
            // is_default intentionally left untouched (OQ6).
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default)
        {
            var index = Accounts.FindIndex(entry => entry.UserUuid == userUuid && entry.Account.Uuid == bankAccountUuid);
            if (index < 0)
                return Task.FromResult(false);

            var wasDefault = Accounts[index].Account.IsDefault;
            Accounts.RemoveAt(index);

            if (wasDefault)
            {
                var promote = Accounts.Where(entry => entry.UserUuid == userUuid).Select(entry => entry.Account).FirstOrDefault();
                if (promote is not null)
                    promote.IsDefault = true;
            }

            return Task.FromResult(true);
        }

        public Task<bool> SetDefaultAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default)
        {
            var target = Accounts
                .Where(entry => entry.UserUuid == userUuid && entry.Account.Uuid == bankAccountUuid)
                .Select(entry => entry.Account)
                .FirstOrDefault();
            if (target is null)
                return Task.FromResult(false);

            foreach (var entry in Accounts.Where(entry => entry.UserUuid == userUuid))
                entry.Account.IsDefault = false;
            target.IsDefault = true;
            return Task.FromResult(true);
        }

        public IQueryable<BankAccount> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(
            Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(
            Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
