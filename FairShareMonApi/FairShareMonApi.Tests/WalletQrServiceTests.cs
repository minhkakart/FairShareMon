using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Banks;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Repositories;
using FairShareMonApi.Services.Api.Banks;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Tests.Infrastructure;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>WalletQrService</c> over fakes for the wallet repo / expense service / stats
/// service plus the REAL <see cref="VietQrPayloadBuilder"/> and a capturing fake image service (no DB).
/// Proves the orchestration: destination resolution (default vs owned override; miss -> 12000; none ->
/// 12001); the expense QR composes one entry per still-owing member (an unsettled, non-zero share owed by
/// a non-payer member; amount = that share) - all cleared -> 12003; event QR closed-only (open -> 12002),
/// nobody-owes -> 12003, else exactly one composite entry per negative-balance member with amount =
/// |Balance| and a label carrying the member name. Neither header carries an amount.
/// </summary>
public class WalletQrServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-00000000c001";
    private const string ExpenseUuid = "0198a5c2-0000-7000-8000-0000000e0001";
    private const string EventUuid = "0198a5c2-0000-7000-8000-0000000e0002";
    private const string PayerUuid = "0198a5c2-0000-7000-8000-0000000e00a0";

    private readonly FakeBankAccountRepository _accounts = new();
    private readonly FakeExpensesService _expenses = new();
    private readonly FakeStatsService _stats = new();
    private readonly CapturingQrImageService _images = new();
    // Pass-through tier double for the orchestration tests; the Premium QR gate is proved separately
    // (below + at the endpoint level).
    private readonly FakeTierService _tier = new();
    // Directory seeded with the default test account's BIN (970436) -> a distinct branded ShortName, so a
    // hit resolves to the ShortName (branding proven against the account's own saved BankName).
    private readonly FakeBankDirectoryService _bankDirectory = new()
    {
        Banks =
        {
            new BankResponse
            {
                Bin = "970436", Code = "VCB",
                Name = "Ngân hàng TMCP Ngoại thương Việt Nam", ShortName = "Vietcombank", LogoUrl = ""
            }
        }
    };

    // The localizer ctor param is optional (falls back to SharedStringLocalizer.Instance), so it is omitted
    // here - header label text is asserted in LocalizationResourceTests; these tests assert header structure.
    private WalletQrService CreateService() =>
        new(_accounts, _expenses, _stats, _tier,
            new StubQrContentProviderResolver(new LocalQrContentProvider(new VietQrPayloadBuilder())),
            _images, _bankDirectory);

    private BankAccount AddDefaultAccount(string bin = "970436", string number = "0123456789")
    {
        var account = new BankAccount
        {
            BankBin = bin, BankName = "Vietcombank", AccountNumber = number,
            AccountHolderName = "Nguyen Van A", IsDefault = true
        };
        _accounts.Accounts.Add(account);
        return account;
    }

    // ---- Destination resolution -------------------------------------------------------------------

    [Fact]
    public async Task GenerateExpenseQr_NoBankAccount_Throws12001()
    {
        _expenses.Expense = ExpenseWith(Share("Cường", 500_000m));

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.NoBankAccountForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseQr_OverrideAccountMiss_Throws12000()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(Share("Cường", 500_000m));

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, "no-such-account"));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseQr_OverrideAccountOwned_UsesTheOverrideDestination()
    {
        AddDefaultAccount(bin: "970436", number: "0123456789");
        var other = new BankAccount
        {
            BankBin = "970422", BankName = "MB Bank", AccountNumber = "9998887776",
            AccountHolderName = "Tran Thi B", IsDefault = false
        };
        _accounts.Accounts.Add(other);
        _expenses.Expense = ExpenseWith(Share("Cường", 500_000m));

        await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, other.Uuid);

        var items = Assert.Single(_images.CompositeBatches);
        var item = Assert.Single(items);
        var beneficiary = ParseTlv(ParseTlv(ParseTlv(item.Payload)["38"])["01"]);
        Assert.Equal("970422", beneficiary["00"]); // override BIN, not the default's
        Assert.Equal("9998887776", beneficiary["01"]);
    }

    // ---- Expense QR ------------------------------------------------------------------------------

    [Fact]
    public async Task GenerateExpenseQr_Default_ComposesOneQrPerUnsettledNonPayerShare()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m),
            Share("Dũng", 250_000m));

        var result = await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null);

        Assert.Equal("image/png", result.ContentType);
        Assert.Contains(ExpenseUuid, result.FileName);

        var items = Assert.Single(_images.CompositeBatches); // one composite call
        Assert.Equal(2, items.Count);                        // one per billable share

        // Amount per member = their share; label carries the member name.
        var cuong = items.Single(item => item.Label.Contains("Cường"));
        Assert.Equal("500000", ParseTlv(cuong.Payload)["54"]);

        var dung = items.Single(item => item.Label.Contains("Dũng"));
        Assert.Equal("250000", ParseTlv(dung.Payload)["54"]);
    }

    [Fact]
    public async Task GenerateExpenseQr_ExcludesSettledZeroAndPayerOwnShares()
    {
        // Only Cường's unsettled, non-zero, non-payer share is billed: the settled share, the 0đ share,
        // and the payer's own share all drop out (mirrors the event's "still owing" filter, per-member).
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m),                // billed
            Share("Dũng", 250_000m, settled: true),  // settled -> excluded
            Share("Én", 0m),                          // zero -> excluded
            PayerShare(300_000m));                    // payer's own share -> excluded

        await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null);

        var items = Assert.Single(_images.CompositeBatches);
        var only = Assert.Single(items);
        Assert.Contains("Cường", only.Label);
        Assert.Equal("500000", ParseTlv(only.Payload)["54"]);
    }

    [Fact]
    public async Task GenerateExpenseQr_AllSharesSettledOrPayerOnly_Throws12003()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m, settled: true), // settled -> excluded
            PayerShare(300_000m));                    // payer's own -> excluded

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.NoOutstandingDebtForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseQr_ExpenseMissBubblesUpAs6000()
    {
        AddDefaultAccount();
        _expenses.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    // ---- Event QR --------------------------------------------------------------------------------

    [Fact]
    public async Task GenerateEventQr_OpenEvent_Throws12002()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = false,
            Rows = [Row("Cường", -500_000m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.EventNotClosedForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_NobodyOwes_Throws12003()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows = [Row("Bình", 300_000m), Row("An", 0m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.NoOutstandingDebtForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_ClosedWithDebtors_ComposesOnePerNegativeBalanceMember()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows =
            [
                Row("Bình", 300_000m),   // positive - excluded
                Row("An", 0m),           // zero - excluded
                Row("Cường", -500_000m), // owing - included
                Row("Dũng", -125_000m)   // owing - included
            ]
        };

        var result = await CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null);

        Assert.Equal("image/png", result.ContentType);
        Assert.Contains(EventUuid, result.FileName);

        var items = Assert.Single(_images.CompositeBatches); // one composite call
        Assert.Equal(2, items.Count);                        // exactly the two debtors

        // Amount per debtor = |Balance|; label carries the member name.
        var cuong = items.Single(item => item.Label.Contains("Cường"));
        Assert.Equal("500000", ParseTlv(cuong.Payload)["54"]);

        var dung = items.Single(item => item.Label.Contains("Dũng"));
        Assert.Equal("125000", ParseTlv(dung.Payload)["54"]);
    }

    [Fact]
    public async Task GenerateEventQr_SettledOwingMember_ExcludedFromComposite()
    {
        // Two owing members (negative balance); Cường has cleared his net debt (Layer B), so his
        // Outstanding is 0 and the QR (billing on Outstanding > 0, OQ13a) must bill only Dũng.
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows =
            [
                SettledRow("Cường", -500_000m), // owing but settled → excluded
                Row("Dũng", -125_000m)          // owing, uncleared → included
            ]
        };

        var result = await CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null);

        var items = Assert.Single(_images.CompositeBatches);
        var only = Assert.Single(items);
        Assert.Contains("Dũng", only.Label);
        Assert.Equal("125000", ParseTlv(only.Payload)["54"]);
    }

    [Fact]
    public async Task GenerateEventQr_AllOwingMembersSettled_Throws12003()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows = [SettledRow("Cường", -500_000m), SettledRow("Dũng", -125_000m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.NoOutstandingDebtForQr, exception.Code); // widened: no UNCLEARED negative balances
    }

    [Fact]
    public async Task GenerateEventQr_NoBankAccount_Throws12001BeforeBalanceLookup()
    {
        _stats.Balance = new EventBalanceResponse { EventUuid = EventUuid, IsClosed = true, Rows = [Row("Cường", -1m)] };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.NoBankAccountForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_EventMissBubblesUpAs9000()
    {
        AddDefaultAccount();
        _stats.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    // ---- QR image header (bank info + title; amount on the expense header only) -------------------

    [Fact]
    public async Task GenerateExpenseQr_Image_HeaderCarriesTitleBrandedBankHolderNumberAndNoAmount()
    {
        AddDefaultAccount(bin: "970436", number: "0123456789"); // BankName "Vietcombank", holder "Nguyen Van A"
        _expenses.Expense = ExpenseWith(Share("Cường", 500_000m));

        await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null);

        var header = Assert.Single(_images.CompositeHeaders);
        Assert.Equal("Ăn tối", header.Title);                 // title = expense name
        Assert.Equal("Vietcombank", header.BankName);         // branded directory ShortName by BIN
        Assert.Equal("Nguyen Van A", header.AccountHolderName);
        Assert.Equal("0123456789", header.AccountNumber);
        // Expense header omits the amount (per-member amounts live under each member's QR).
        Assert.Null(header.AmountLabel);
        Assert.Null(header.AmountText);
    }

    [Fact]
    public async Task GenerateEventQr_Image_HeaderCarriesEventNameAndNoAmount()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows = [Row("Cường", -500_000m), Row("Dũng", -125_000m)]
        };

        await CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null);

        var header = Assert.Single(_images.CompositeHeaders);
        Assert.Equal("Đà Lạt", header.Title);        // title = event name
        Assert.Equal("Vietcombank", header.BankName); // branded directory ShortName
        // Event header omits the amount entirely (per-member amounts live under each member's QR).
        Assert.Null(header.AmountLabel);
        Assert.Null(header.AmountText);
    }

    [Fact]
    public async Task GenerateExpenseQr_BinInDirectory_HeaderUsesBrandedShortNameOverSavedBankName()
    {
        // Account's own saved BankName differs from the directory ShortName -> ShortName must win (branding).
        var account = new BankAccount
        {
            BankBin = "970436", BankName = "Ngoai Thuong (saved)", AccountNumber = "0123456789",
            AccountHolderName = "Nguyen Van A", IsDefault = true
        };
        _accounts.Accounts.Add(account);
        _expenses.Expense = ExpenseWith(Share("Cường", 100_000m));

        await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null);

        var header = Assert.Single(_images.CompositeHeaders);
        Assert.Equal("Vietcombank", header.BankName); // ShortName, not "Ngoai Thuong (saved)"
    }

    [Fact]
    public async Task GenerateExpenseQr_BinNotInDirectory_HeaderFallsBackToSavedBankName()
    {
        // A BIN the directory doesn't carry -> fall back to the account's saved BankName.
        var account = new BankAccount
        {
            BankBin = "999999", BankName = "My Saved Bank", AccountNumber = "5554443332",
            AccountHolderName = "Tran Thi B", IsDefault = true
        };
        _accounts.Accounts.Add(account);
        _expenses.Expense = ExpenseWith(Share("Cường", 100_000m));

        await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null);

        var header = Assert.Single(_images.CompositeHeaders);
        Assert.Equal("My Saved Bank", header.BankName);
    }

    [Fact]
    public async Task GenerateExpenseQr_BinInDirectoryButShortNameBlank_HeaderFallsBackToDirectoryName()
    {
        // Directory hit whose ShortName is blank -> fall through to the directory Name (not the saved name).
        _bankDirectory.Banks.Clear();
        _bankDirectory.Banks.Add(new BankResponse
        {
            Bin = "970999", Code = "XYZ", Name = "Ngân hàng Đầy Đủ", ShortName = "", LogoUrl = ""
        });
        var account = new BankAccount
        {
            BankBin = "970999", BankName = "Saved Name", AccountNumber = "1112223334",
            AccountHolderName = "Le Van C", IsDefault = true
        };
        _accounts.Accounts.Add(account);
        _expenses.Expense = ExpenseWith(Share("Cường", 100_000m));

        await CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null);

        var header = Assert.Single(_images.CompositeHeaders);
        Assert.Equal("Ngân hàng Đầy Đủ", header.BankName); // Name, not "Saved Name"
    }

    // ---- M10 Premium feature-gate (both QR ops gated, fires before anything is resolved) ----------

    [Fact]
    public async Task GenerateExpenseQr_FreeCaller_Throws13003BeforeResolvingDestination()
    {
        // No bank account added: if the gate did NOT fire first, destination resolution would throw
        // 12001 instead - asserting 13003 proves the gate runs before anything is resolved.
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    [Fact]
    public async Task GenerateEventQr_FreeCaller_Throws13003BeforeResolvingDestination()
    {
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventQrAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    // ---- Per-member expense QR list (GenerateExpenseMemberQrsAsync) -------------------------------

    [Fact]
    public async Task GenerateExpenseMemberQrs_Default_OneEntryPerBilledMemberInSharesOrder()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m),
            Share("Dũng", 250_000m));

        var result = await CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null);

        Assert.Equal(2, result.Count); // one per billable share

        // Order preserved from expense.Shares (Decision 5).
        Assert.Equal("Cường", result[0].MemberName);
        Assert.Equal(500_000m, result[0].Amount);
        Assert.Equal(_expenses.Expense.Shares[0].Member.Uuid, result[0].MemberUuid);
        Assert.StartsWith("data:image/png;base64,", result[0].Image);

        Assert.Equal("Dũng", result[1].MemberName);
        Assert.Equal(250_000m, result[1].Amount);
        Assert.Equal(_expenses.Expense.Shares[1].Member.Uuid, result[1].MemberUuid);
        Assert.StartsWith("data:image/png;base64,", result[1].Image);

        // Per-member path renders via RenderSingle -> one captured payload per billed member, in order.
        Assert.Equal(2, _images.SinglePayloads.Count);
        Assert.Empty(_images.CompositeBatches); // NOT the composite path
        Assert.Equal("500000", ParseTlv(_images.SinglePayloads[0])["54"]); // amount rides each own payload
        Assert.Equal("250000", ParseTlv(_images.SinglePayloads[1])["54"]);
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_ExcludesSettledZeroAndPayerOwnShares()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m),                // billed
            Share("Dũng", 250_000m, settled: true),  // settled -> excluded
            Share("Én", 0m),                          // zero -> excluded
            PayerShare(300_000m));                    // payer's own share -> excluded

        var result = await CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null);

        var only = Assert.Single(result);
        Assert.Equal("Cường", only.MemberName);
        Assert.Equal(500_000m, only.Amount);
        Assert.Equal("500000", ParseTlv(Assert.Single(_images.SinglePayloads))["54"]);
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_AllSharesSettledZeroOrPayerOnly_Throws12003()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m, settled: true), // settled -> excluded
            Share("Én", 0m),                          // zero -> excluded
            PayerShare(300_000m));                    // payer's own -> excluded

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.NoOutstandingDebtForQr, exception.Code);
        Assert.Empty(_images.SinglePayloads); // nothing rendered when nobody owes
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_HeaderPerMemberCarriesTitleAndMemberAmount()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m),
            Share("Dũng", 250_000m));

        await CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null);

        Assert.Equal(2, _images.SingleHeaders.Count);

        // Title = "{expenseName} - {memberName}" (Open Question 2a); amount row = the member's amount.
        Assert.Equal("Ăn tối - Cường", _images.SingleHeaders[0].Title);
        Assert.NotNull(_images.SingleHeaders[0].AmountLabel);
        Assert.Equal("500.000đ", _images.SingleHeaders[0].AmountText);

        Assert.Equal("Ăn tối - Dũng", _images.SingleHeaders[1].Title);
        Assert.Equal("250.000đ", _images.SingleHeaders[1].AmountText);

        // Branded bank fields still ride each header.
        Assert.Equal("Vietcombank", _images.SingleHeaders[0].BankName);
        Assert.Equal("Nguyen Van A", _images.SingleHeaders[0].AccountHolderName);
        Assert.Equal("0123456789", _images.SingleHeaders[0].AccountNumber);
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_Image_DecodesToPngMagicBytes()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(Share("Cường", 500_000m));

        var result = await CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null);

        var only = Assert.Single(result);
        const string prefix = "data:image/png;base64,";
        Assert.StartsWith(prefix, only.Image);
        var bytes = Convert.FromBase64String(only.Image[prefix.Length..]);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes); // PNG magic (CapturingQrImageService output)
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_NoBankAccount_Throws12001()
    {
        _expenses.Expense = ExpenseWith(Share("Cường", 500_000m));

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.NoBankAccountForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_OverrideAccountMiss_Throws12000()
    {
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(Share("Cường", 500_000m));

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, "no-such-account"));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_ExpenseMiss_Throws6000()
    {
        AddDefaultAccount();
        _expenses.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_FreeCaller_Throws13003BeforeResolvingDestination()
    {
        // No bank account added: 13003 (not 12001) proves the Premium gate runs before resolution.
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    [Fact]
    public async Task GenerateExpenseMemberQrs_SharesSameBilledSetAsCompositePath()
    {
        // Parity: the per-member list and the composite items cover the SAME members/amounts (shared
        // CollectExpenseBillables) for the same seeded input.
        AddDefaultAccount();
        _expenses.Expense = ExpenseWith(
            Share("Cường", 500_000m),
            Share("Dũng", 250_000m, settled: true), // excluded from both
            Share("Én", 125_000m));

        var service = CreateService();
        var members = await service.GenerateExpenseMemberQrsAsync(UserUuid, ExpenseUuid, null);
        await service.GenerateExpenseQrAsync(UserUuid, ExpenseUuid, null); // composite path, same seeded input
        var composite = Assert.Single(_images.CompositeBatches);

        Assert.Equal(composite.Count, members.Count);
        var memberNames = members.Select(m => m.MemberName).ToArray();
        Assert.Equal(new[] { "Cường", "Én" }, memberNames);
        // Composite labels carry the same member names, in the same order (shared billed set).
        Assert.Contains("Cường", composite[0].Label);
        Assert.Contains("Én", composite[1].Label);
    }

    // ---- Per-member event QR list (GenerateEventMemberQrsAsync) -----------------------------------

    [Fact]
    public async Task GenerateEventMemberQrs_ClosedWithDebtors_OneEntryPerOutstandingMemberInRowsOrder()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows =
            [
                Row("Bình", 300_000m),   // positive -> excluded
                Row("An", 0m),           // zero -> excluded
                Row("Cường", -500_000m), // owing -> included
                Row("Dũng", -125_000m)   // owing -> included
            ]
        };

        var result = await CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, null);

        Assert.Equal(2, result.Count);

        // Order preserved from balance.Rows (Decision 5); amount = Outstanding = |balance|.
        Assert.Equal("Cường", result[0].MemberName);
        Assert.Equal(500_000m, result[0].Amount);
        Assert.StartsWith("data:image/png;base64,", result[0].Image);

        Assert.Equal("Dũng", result[1].MemberName);
        Assert.Equal(125_000m, result[1].Amount);

        Assert.Equal(2, _images.SinglePayloads.Count);
        Assert.Empty(_images.CompositeBatches);
        Assert.Equal("500000", ParseTlv(_images.SinglePayloads[0])["54"]);
        Assert.Equal("125000", ParseTlv(_images.SinglePayloads[1])["54"]);

        // Header title carries event name + member; amount row = the member's outstanding.
        Assert.Equal("Đà Lạt - Cường", _images.SingleHeaders[0].Title);
        Assert.Equal("500.000đ", _images.SingleHeaders[0].AmountText);
    }

    [Fact]
    public async Task GenerateEventMemberQrs_ExcludesSettledAndClearedMembers()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows =
            [
                SettledRow("Cường", -500_000m), // owing but cleared (Outstanding 0) -> excluded
                Row("Dũng", -125_000m)          // owing, uncleared -> included
            ]
        };

        var result = await CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, null);

        var only = Assert.Single(result);
        Assert.Equal("Dũng", only.MemberName);
        Assert.Equal(125_000m, only.Amount);
        Assert.Equal("125000", ParseTlv(Assert.Single(_images.SinglePayloads))["54"]);
    }

    [Fact]
    public async Task GenerateEventMemberQrs_AllCleared_Throws12003()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
            Rows = [SettledRow("Cường", -500_000m), Row("Bình", 300_000m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.NoOutstandingDebtForQr, exception.Code);
        Assert.Empty(_images.SinglePayloads);
    }

    [Fact]
    public async Task GenerateEventMemberQrs_OpenEvent_Throws12002()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = false,
            Rows = [Row("Cường", -500_000m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.EventNotClosedForQr, exception.Code);
        Assert.Empty(_images.SinglePayloads);
    }

    [Fact]
    public async Task GenerateEventMemberQrs_EventMiss_Throws9000()
    {
        AddDefaultAccount();
        _stats.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task GenerateEventMemberQrs_NoBankAccount_Throws12001BeforeBalanceLookup()
    {
        _stats.Balance = new EventBalanceResponse { EventUuid = EventUuid, IsClosed = true, Rows = [Row("Cường", -1m)] };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.NoBankAccountForQr, exception.Code);
    }

    [Fact]
    public async Task GenerateEventMemberQrs_OverrideAccountMiss_Throws12000()
    {
        AddDefaultAccount();
        _stats.Balance = new EventBalanceResponse
        {
            EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true, Rows = [Row("Cường", -500_000m)]
        };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, "no-such-account"));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task GenerateEventMemberQrs_FreeCaller_Throws13003BeforeResolvingDestination()
    {
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GenerateEventMemberQrsAsync(UserUuid, EventUuid, null));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
    }

    // An expense paid by "An" (PayerUuid); the passed shares are the debtor shares. Total is the SUM.
    private static ExpenseResponse ExpenseWith(params ShareResponse[] shares) =>
        new()
        {
            Uuid = ExpenseUuid, Name = "Ăn tối",
            Payer = new MemberResponse { Uuid = PayerUuid, Name = "An" },
            Total = shares.Sum(share => share.Amount), Shares = shares
        };

    // A share owed by a distinct (non-payer) member; unsettled unless stated otherwise.
    private static ShareResponse Share(string name, decimal amount, bool settled = false) =>
        new()
        {
            Uuid = Guid.NewGuid().ToString(),
            Member = new MemberResponse { Uuid = Guid.NewGuid().ToString(), Name = name },
            Amount = amount, IsSettled = settled
        };

    // A share owed by the payer themselves - the QR must exclude it (the payer never transfers to self).
    private static ShareResponse PayerShare(decimal amount) =>
        new()
        {
            Uuid = Guid.NewGuid().ToString(),
            Member = new MemberResponse { Uuid = PayerUuid, Name = "An" },
            Amount = amount
        };

    // Mirrors the StatsService overlay derivation (settled-per-member OQ8a): an unsettled owing member's
    // outstanding is -balance, otherwise 0. The event QR bills on Outstanding > 0 (OQ13a).
    private static MemberBalanceRow Row(string name, decimal balance) =>
        new() { MemberUuid = Guid.NewGuid().ToString(), MemberName = name, Balance = balance, Outstanding = balance < 0m ? -balance : 0m };

    // An owing member who has cleared their net debt (Layer B settled): balance stays negative but the
    // derived Outstanding is 0, so the QR must skip them (settled-per-member OQ13a).
    private static MemberBalanceRow SettledRow(string name, decimal balance) =>
        new() { MemberUuid = Guid.NewGuid().ToString(), MemberName = name, Balance = balance, IsSettled = true, SettledAt = DateTime.UtcNow, Outstanding = 0m };

    private static Dictionary<string, string> ParseTlv(string data)
    {
        var map = new Dictionary<string, string>();
        var i = 0;
        while (i < data.Length)
        {
            var id = data.Substring(i, 2);
            var length = int.Parse(data.Substring(i + 2, 2));
            map[id] = data.Substring(i + 4, length);
            i += 4 + length;
        }

        return map;
    }

    // ---- Fakes -----------------------------------------------------------------------------------

    // Resolves to a single QR content provider (the Local one over the real VietQrPayloadBuilder), so the
    // existing byte-for-byte payload assertions are preserved after WalletQrService moved onto the resolver.
    private sealed class StubQrContentProviderResolver(IQrContentProvider provider) : IQrContentProviderResolver
    {
        public IQrContentProvider Resolve() => provider;
    }

    private sealed class FakeBankAccountRepository : IBankAccountRepository
    {
        public List<BankAccount> Accounts { get; } = [];

        public Task<BankAccount?> GetByUuidAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.FirstOrDefault(account => account.Uuid == bankAccountUuid));

        public Task<BankAccount?> GetDefaultAsync(string userUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Accounts.FirstOrDefault(account => account.IsDefault));

        public Task<IReadOnlyList<BankAccount>> ListByUserAsync(string userUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BankAccount?> CreateAsync(string userUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(string userUuid, string bankAccountUuid, string bankBin, string bankName, string accountNumber, string accountHolderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetDefaultAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IQueryable<BankAccount> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();
        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeExpensesService : IExpensesService
    {
        public ExpenseResponse? Expense { get; set; }
        public bool ThrowNotFound { get; set; }

        public Task<ExpenseResponse> GetAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFound)
                throw new ErrorException(ErrorCodes.ExpenseNotFound, "Không tìm thấy phiếu chi tiêu.");
            return Task.FromResult(Expense!);
        }

        public Task<IReadOnlyList<ExpenseSummaryResponse>> ListAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> CreateAsync(string userUuid, CreateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> UpdateAsync(string userUuid, string expenseUuid, UpdateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSettledAsync(string userUuid, string expenseUuid, SetSettledRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> AssignEventAsync(string userUuid, string expenseUuid, AssignEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditLogResponse>> GetHistoryAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStatsService : IStatsService
    {
        public EventBalanceResponse? Balance { get; set; }
        public bool ThrowNotFound { get; set; }

        public Task<EventBalanceResponse> GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            if (ThrowNotFound)
                throw new ErrorException(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
            return Task.FromResult(Balance!);
        }

        public Task<OverviewStatsResponse> GetOverviewAsync(string userUuid, StatsRangeRequest range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ByCategoryStatsResponse> GetByCategoryAsync(string userUuid, ByCategoryStatsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingQrImageService : IQrImageService
    {
        public List<string> SinglePayloads { get; } = [];
        public List<QrHeader> SingleHeaders { get; } = [];
        public List<IReadOnlyList<QrCompositeItem>> CompositeBatches { get; } = [];
        public List<QrHeader> CompositeHeaders { get; } = [];

        public byte[] RenderSingle(string payload, QrHeader header)
        {
            SinglePayloads.Add(payload);
            SingleHeaders.Add(header);
            return [0x89, 0x50, 0x4E, 0x47];
        }

        public byte[] RenderComposite(IReadOnlyList<QrCompositeItem> items, QrHeader header)
        {
            CompositeBatches.Add(items);
            CompositeHeaders.Add(header);
            return [0x89, 0x50, 0x4E, 0x47];
        }
    }

    // Serves a mutable in-memory bank list (mirrors the fake in VietQrRemoteQrContentProviderTests); never
    // caches, never throws - just what WalletQrService.ResolveBankNameAsync reads by BIN.
    private sealed class FakeBankDirectoryService : IBankDirectoryService
    {
        public List<BankResponse> Banks { get; } = [];

        public Task<IReadOnlyList<BankResponse>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankResponse>>(Banks);
    }
}
