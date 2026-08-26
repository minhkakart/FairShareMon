using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Categories;
using FairShareMonApi.Models.Events;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Models.Members;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Models.Share;
using FairShareMonApi.Models.Stats;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Events;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Services.Api.Share;
using FairShareMonApi.Services.Api.Stats;
using FairShareMonApi.Services.Api.Wallet;
using FairShareMonApi.Tests.Infrastructure;
using FairShareMonApi.Utils;
using FairShareMonApi.Validators.EventShare;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="EventShareService"/> over fakes for the repo / stats / events /
/// expenses / bank-account repo / wallet-QR / tier collaborators, the REAL
/// <see cref="CreateShareLinkRequestValidator"/>, and a REAL <see cref="EventShareLinkCache"/> wired
/// over the fake repo and the unreachable Redis multiplexer (so every cache op degrades to the DB
/// fallback deterministically, exactly like production when Redis is down). No DB, no HTTP.
///
/// Proves the create ordering (Premium gate 13003 FIRST, before any resolution; then closed-only 16001
/// and event-miss 9000), bank-snapshot resolution (explicit-miss 12000; no default/override =&gt; HasQr
/// false; present =&gt; snapshot copied), reuse (same token, no new row, fixed TTL) vs regenerate (old
/// revoked, fresh token), get-active (null when none, 9000 on ownership miss), idempotent revoke, and
/// the anonymous LIVE public read/QR (16000 for unknown/expired/revoked; a re-read reflects a changed
/// overlay; QR delegates to the share-QR path using the OWNER uuid from the token, never a caller).
/// </summary>
public class EventShareServiceTests
{
    private const string OwnerUuid = "0198a5c2-0000-7000-8000-00000000c001";
    private const string EventUuid = "0198a5c2-0000-7000-8000-0000000e0002";

    private readonly FakeEventShareLinkRepository _repo = new();
    private readonly FakeStatsService _stats = new();
    private readonly FakeEventsService _events = new();
    private readonly FakeExpensesService _expenses = new();
    private readonly FakeBankAccountRepository _accounts = new();
    private readonly FakeWalletQrService _walletQr = new();
    private readonly FakeTierService _tier = new();
    private readonly CreateShareLinkRequestValidator _validator = new();
    private readonly RecordingStreamBroadcaster _streamBroadcaster = new();

    private EventShareService CreateService() =>
        new(_repo, CreateCache(), _stats, _events, _expenses, _accounts, _walletQr, _tier, _validator, Config(), _streamBroadcaster);

    private EventShareLinkCache CreateCache() =>
        new(_repo, UnreachableRedis.Instance, NullLogger<EventShareLinkCache>.Instance);

    private static IConfiguration Config(int ttlHours = 24) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Share:LinkTtlHours"] = ttlHours.ToString() })
            .Build();

    private static EventBalanceResponse ClosedBalance(params MemberBalanceRow[] rows) => new()
    {
        EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = true,
        Rows = rows,
        TotalOutstanding = rows.Sum(row => row.Outstanding),
        OwingMemberCount = rows.Count(row => row.Outstanding > 0m),
        SettledMemberCount = rows.Count(row => row.IsSettled)
    };

    private static MemberBalanceRow OwingRow(string name, decimal balance) => new()
    {
        MemberUuid = Guid.NewGuid().ToString(), MemberName = name, Balance = balance,
        Outstanding = balance < 0m ? -balance : 0m
    };

    private void SeedDefaultAccount() => _accounts.Accounts.Add(new BankAccount
    {
        BankBin = "970436", BankName = "Vietcombank", AccountNumber = "0123456789",
        AccountHolderName = "Nguyen Van A", IsDefault = true
    });

    // ---------------------------- Create: ordering + guards ----------------------------

    [Fact]
    public async Task CreateAsync_FreeCaller_Throws13003BeforeAnyResolution()
    {
        // Neither balance nor account is seeded: were the gate NOT first, the service would throw 9000
        // (event miss). Asserting 13003 proves the Premium gate runs before anything is resolved.
        _tier.PremiumFeatureCode = ErrorCodes.PremiumFeatureRequired;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest()));

        Assert.Equal(ErrorCodes.PremiumFeatureRequired, exception.Code);
        Assert.Equal(0, _repo.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_EventMiss_Throws9000()
    {
        _stats.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest()));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    [Fact]
    public async Task CreateAsync_OpenEvent_Throws16001()
    {
        _stats.Balance = new EventBalanceResponse { EventUuid = EventUuid, EventName = "Đà Lạt", IsClosed = false, Rows = [] };

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest()));

        Assert.Equal(ErrorCodes.EventNotClosedForShare, exception.Code);
    }

    [Fact]
    public async Task CreateAsync_ExplicitBankOverrideMiss_Throws12000()
    {
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest { BankAccountUuid = "no-such-account" }));

        Assert.Equal(ErrorCodes.BankAccountNotFound, exception.Code);
    }

    [Fact]
    public async Task CreateAsync_NoDefaultAndNoOverride_CreatesLinkWithHasQrFalse()
    {
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));

        var response = await CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest());

        Assert.False(response.HasQr);
        Assert.Null(response.BankName);
        var link = Assert.Single(_repo.Links);
        Assert.Null(link.BankBin);
        Assert.Null(link.BankAccountUuid);
        Assert.False(string.IsNullOrWhiteSpace(response.Token));
    }

    [Fact]
    public async Task CreateAsync_DefaultAccount_SnapshotsBankFieldsAndSetsFixedTtl()
    {
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));
        SeedDefaultAccount();
        var before = AppDateTime.Now;

        var response = await CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest());

        Assert.True(response.HasQr);
        Assert.Equal("Vietcombank", response.BankName);
        Assert.Equal("0123456789", response.AccountNumber);
        Assert.Equal("Nguyen Van A", response.AccountHolderName);

        var link = Assert.Single(_repo.Links);
        Assert.Equal("970436", link.BankBin);
        Assert.Equal("Vietcombank", link.BankName);
        Assert.Equal("0123456789", link.AccountNumber);
        Assert.Equal("Nguyen Van A", link.AccountHolderName);

        // ExpiresAt = creation + 24h (fixed TTL, OQ7a).
        Assert.InRange(response.ExpiresAt, before.AddHours(24).AddSeconds(-30), AppDateTime.Now.AddHours(24).AddSeconds(30));
    }

    [Fact]
    public async Task CreateAsync_LinkIsResolvableByItsTokenAfterCreation()
    {
        // Proves the create wrote a row the public lookup can resolve (the cache primes via AddAsync and
        // self-heals from the DB row on a miss - here Redis is down, so the DB row alone must answer).
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));
        SeedDefaultAccount();
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true, ClosedAt = AppDateTime.Now };

        var service = CreateService();
        var created = await service.CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest());
        var report = await service.GetPublicAsync(created.Token);

        Assert.Equal("Đà Lạt", report.EventName);
        Assert.True(report.HasQr);
    }

    // ---------------------------- Reuse vs regenerate ----------------------------

    [Fact]
    public async Task CreateAsync_ActiveLinkExists_ReusesSameTokenWithoutNewRow()
    {
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));
        var existing = await _repo.CreateAsync(OwnerUuid, EventUuid, "existing-token", AppDateTime.Now.AddHours(24), null, null, null, null, null);
        _repo.CreateCallCount = 0; // reset the setup call

        var response = await CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest { BankAccountUuid = "ignored-while-active" });

        Assert.Equal("existing-token", response.Token);
        Assert.Equal(existing.ExpiresAt, response.ExpiresAt); // fixed TTL, not extended (OQ7a)
        Assert.Equal(0, _repo.CreateCallCount);                // no duplicate minted (Decision 4)
        Assert.Single(_repo.Links);
    }

    [Fact]
    public async Task CreateAsync_Regenerate_RevokesOldAndMintsFreshToken()
    {
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));
        var old = await _repo.CreateAsync(OwnerUuid, EventUuid, "old-token", AppDateTime.Now.AddHours(24), null, null, null, null, null);

        var response = await CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest { Regenerate = true });

        Assert.NotEqual("old-token", response.Token);
        Assert.NotNull(old.RevokedAt);                 // old link soft-revoked
        Assert.Equal(2, _repo.Links.Count);            // old (revoked) + fresh
        Assert.Single(_repo.Links, link => link.RevokedAt == null && link.Token == response.Token);
    }

    [Fact]
    public async Task CreateAsync_Regenerate_PublishesRevokedOnOldTokenBeforeReturningNewOne()
    {
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));
        await _repo.CreateAsync(OwnerUuid, EventUuid, "old-token", AppDateTime.Now.AddHours(24), null, null, null, null, null);

        var response = await CreateService().CreateAsync(OwnerUuid, EventUuid, new CreateShareLinkRequest { Regenerate = true });

        Assert.Equal(1, _streamBroadcaster.PublishRevokedCalls);
        Assert.Equal("old-token", _streamBroadcaster.LastRevokedToken); // terminates any live stream on the OLD token
        Assert.NotEqual("old-token", response.Token);                  // ...before returning the new one
    }

    // ---------------------------- GetActive ----------------------------

    [Fact]
    public async Task GetActiveAsync_ActiveLink_ReturnsIt()
    {
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };
        await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), "acc", "970436", "Vietcombank", "0123456789", "Nguyen Van A");

        var response = await CreateService().GetActiveAsync(OwnerUuid, EventUuid);

        Assert.NotNull(response);
        Assert.Equal("tok", response!.Token);
        Assert.True(response.HasQr);
    }

    [Fact]
    public async Task GetActiveAsync_NoActiveLink_ReturnsNull()
    {
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };

        var response = await CreateService().GetActiveAsync(OwnerUuid, EventUuid);

        Assert.Null(response); // "not shared yet" is a normal state (OQ8a), not an error
    }

    [Fact]
    public async Task GetActiveAsync_OwnershipMiss_Throws9000()
    {
        _events.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetActiveAsync(OwnerUuid, EventUuid));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    // ---------------------------- Revoke ----------------------------

    [Fact]
    public async Task RevokeAsync_ActiveLink_SetsRevokedAt()
    {
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };
        var link = await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), null, null, null, null, null);

        await CreateService().RevokeAsync(OwnerUuid, EventUuid);

        Assert.NotNull(link.RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_NoActiveLink_IsIdempotentSuccess()
    {
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };

        await CreateService().RevokeAsync(OwnerUuid, EventUuid); // no throw
    }

    [Fact]
    public async Task RevokeAsync_ActiveLink_PublishesRevokedWithThatToken()
    {
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };
        await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), null, null, null, null, null);

        await CreateService().RevokeAsync(OwnerUuid, EventUuid);

        Assert.Equal(1, _streamBroadcaster.PublishRevokedCalls);
        Assert.Equal("tok", _streamBroadcaster.LastRevokedToken); // terminates any live stream on the just-revoked token
    }

    [Fact]
    public async Task RevokeAsync_NoActiveLink_NeverPublishesRevoked()
    {
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };

        await CreateService().RevokeAsync(OwnerUuid, EventUuid); // idempotent no-op

        Assert.Equal(0, _streamBroadcaster.PublishRevokedCalls);
    }

    [Fact]
    public async Task RevokeAsync_OwnershipMiss_Throws9000()
    {
        _events.ThrowNotFound = true;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().RevokeAsync(OwnerUuid, EventUuid));

        Assert.Equal(ErrorCodes.EventNotFound, exception.Code);
    }

    // ---------------------------- GetPublic (anonymous, LIVE) ----------------------------

    [Fact]
    public async Task GetPublicAsync_UnknownToken_Throws16000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetPublicAsync("no-such-token"));

        Assert.Equal(ErrorCodes.ShareLinkNotFoundOrExpired, exception.Code);
    }

    [Fact]
    public async Task GetPublicAsync_ExpiredToken_Throws16000()
    {
        await _repo.CreateAsync(OwnerUuid, EventUuid, "expired", AppDateTime.Now.AddHours(-1), null, null, null, null, null);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetPublicAsync("expired"));

        Assert.Equal(ErrorCodes.ShareLinkNotFoundOrExpired, exception.Code);
    }

    [Fact]
    public async Task GetPublicAsync_RevokedToken_Throws16000()
    {
        await _repo.CreateAsync(OwnerUuid, EventUuid, "revoked", AppDateTime.Now.AddHours(24), null, null, null, null, null);
        await _repo.RevokeActiveByEventAsync(OwnerUuid, EventUuid);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetPublicAsync("revoked"));

        Assert.Equal(ErrorCodes.ShareLinkNotFoundOrExpired, exception.Code);
    }

    [Fact]
    public async Task GetPublicAsync_ValidToken_ReturnsLivePayloadUsingOwnerUuidFromToken()
    {
        var owing = OwingRow("Bình", -300_000m);
        _stats.Balance = ClosedBalance(owing);
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true, ClosedAt = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc) };
        _expenses.Detailed = [ExpenseWith("Ăn tối", payerUuid: "an", payerName: "An", ("bh", "Bình", 300_000m, false))];
        await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), "acc", "970436", "Vietcombank", "0123456789", "Nguyen Van A");

        var report = await CreateService().GetPublicAsync("tok");

        Assert.Equal("Đà Lạt", report.EventName);
        Assert.Equal(new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc), report.ClosedAt);
        Assert.Equal(300_000m, report.TotalOutstanding);
        Assert.Equal(1, report.OwingMemberCount);
        Assert.True(report.HasQr);
        Assert.Same(_stats.Balance.Rows, report.Rows);

        // Per-expense breakdown maps to PublicShare (member/amount/settled/note).
        var expense = Assert.Single(report.Expenses);
        Assert.Equal("Ăn tối", expense.Name);
        Assert.Equal("An", expense.PayerName);
        var share = Assert.Single(expense.Shares);
        Assert.Equal("Bình", share.MemberName);
        Assert.Equal(300_000m, share.Amount);
        Assert.False(share.IsSettled);

        // The live read used the OWNER uuid taken from the token, never an anonymous caller.
        Assert.Equal(OwnerUuid, _stats.LastUserUuid);
        Assert.Equal(OwnerUuid, _events.LastUserUuid);
        Assert.Equal(OwnerUuid, _expenses.LastUserUuid);
    }

    [Fact]
    public async Task GetPublicAsync_NoBankSnapshot_HasQrFalse()
    {
        _stats.Balance = ClosedBalance(OwingRow("Bình", -100_000m));
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };
        await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), null, null, null, null, null);

        var report = await CreateService().GetPublicAsync("tok");

        Assert.False(report.HasQr);
    }

    [Fact]
    public async Task GetPublicAsync_ReflectsChangedSettledOverlayOnSecondRead()
    {
        var owing = OwingRow("Bình", -300_000m);
        _stats.Balance = ClosedBalance(owing);
        _events.Event = new EventResponse { Uuid = EventUuid, Name = "Đà Lạt", IsClosed = true };
        await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), null, null, null, null, null);
        var service = CreateService();

        var before = await service.GetPublicAsync("tok");
        Assert.Equal(300_000m, before.TotalOutstanding);
        Assert.Equal(1, before.OwingMemberCount);

        // Owner marks Bình settled: the overlay changes but the frozen spend figures do not.
        var settled = new MemberBalanceRow { MemberUuid = owing.MemberUuid, MemberName = "Bình", Balance = -300_000m, Outstanding = 0m, IsSettled = true, SettledAt = AppDateTime.Now };
        _stats.Balance = ClosedBalance(settled);

        var after = await service.GetPublicAsync("tok");
        Assert.Equal(0m, after.TotalOutstanding);      // LIVE: recomputed on every read (Decision 3)
        Assert.Equal(0, after.OwingMemberCount);
        Assert.Equal(1, after.SettledMemberCount);
    }

    // ---------------------------- GetPublicMemberQrs (anonymous) ----------------------------

    [Fact]
    public async Task GetPublicMemberQrsAsync_UnknownToken_Throws16000()
    {
        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().GetPublicMemberQrsAsync("no-such-token"));

        Assert.Equal(ErrorCodes.ShareLinkNotFoundOrExpired, exception.Code);
    }

    [Fact]
    public async Task GetPublicMemberQrsAsync_NoSnapshot_ReturnsEmptyListWithoutCallingWalletQr()
    {
        await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), null, null, null, null, null);

        var result = await CreateService().GetPublicMemberQrsAsync("tok");

        Assert.Empty(result);
        Assert.Equal(0, _walletQr.ForShareCallCount); // no QR path when the link has no bank snapshot (OQ4b)
    }

    [Fact]
    public async Task GetPublicMemberQrsAsync_WithSnapshot_DelegatesToShareQrPathWithOwnerAndSnapshot()
    {
        await _repo.CreateAsync(OwnerUuid, EventUuid, "tok", AppDateTime.Now.AddHours(24), "acc", "970436", "Vietcombank", "0123456789", "Nguyen Van A");
        _walletQr.ForShareResult = [new MemberQrResponse { MemberUuid = "bh", MemberName = "Bình", Amount = 300_000m, Image = "data:image/png;base64,iVBOR" }];

        var result = await CreateService().GetPublicMemberQrsAsync("tok");

        Assert.Same(_walletQr.ForShareResult, result);
        Assert.Equal(1, _walletQr.ForShareCallCount);
        Assert.Equal(OwnerUuid, _walletQr.LastOwnerUuid);   // OWNER uuid from the token
        Assert.Equal(EventUuid, _walletQr.LastEventUuid);
        Assert.Equal("970436", _walletQr.LastSnapshot!.BankBin); // link's snapshot, not a live wallet lookup
        Assert.Equal("0123456789", _walletQr.LastSnapshot.AccountNumber);
    }

    // ---------------------------- Test data builders ----------------------------

    private static ExpenseResponse ExpenseWith(string name, string payerUuid, string payerName, params (string Uuid, string Name, decimal Amount, bool Settled)[] shares) => new()
    {
        Uuid = Guid.NewGuid().ToString(),
        Name = name,
        ExpenseTime = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc),
        Payer = new MemberResponse { Uuid = payerUuid, Name = payerName },
        Category = new CategoryResponse { Uuid = "cat", Name = "Ăn uống" },
        Total = shares.Sum(share => share.Amount),
        Shares = shares.Select(share => new ShareResponse
        {
            Uuid = Guid.NewGuid().ToString(),
            Member = new MemberResponse { Uuid = share.Uuid, Name = share.Name },
            Amount = share.Amount,
            IsSettled = share.Settled
        }).ToList()
    };

    // ---------------------------- Fakes ----------------------------

    /// <summary>Wraps a REAL <see cref="EventShareStreamBroadcaster"/> (so <c>Subscribe</c>/fan-out still
    /// behaves exactly like production) while additionally counting <c>PublishRevoked</c> calls and the
    /// last token, so a test can assert the exact call the service made without needing its own
    /// subscription plumbing (planning/public-share-sse-updates.md).</summary>
    private sealed class RecordingStreamBroadcaster : IEventShareStreamBroadcaster
    {
        private readonly EventShareStreamBroadcaster _inner = new();

        public int PublishRevokedCalls { get; private set; }
        public string? LastRevokedToken { get; private set; }

        public IEventShareStreamSubscription Subscribe(string token) => _inner.Subscribe(token);

        public void PublishUpdated(string token) => _inner.PublishUpdated(token);

        public void PublishRevoked(string token)
        {
            PublishRevokedCalls++;
            LastRevokedToken = token;
            _inner.PublishRevoked(token);
        }

        public void PublishExpired(string token) => _inner.PublishExpired(token);
    }

    private sealed class FakeEventShareLinkRepository : IEventShareLinkRepository
    {
        public List<EventShareLink> Links { get; } = [];
        public int CreateCallCount { get; set; }

        public Task<EventShareLink> CreateAsync(string userUuid, string eventUuid, string token, DateTime expiresAt,
            string? bankAccountUuid, string? bankBin, string? bankName, string? accountNumber, string? accountHolderName,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            var link = new EventShareLink
            {
                Token = token,
                ExpiresAt = expiresAt,
                BankAccountUuid = bankAccountUuid,
                BankBin = bankBin,
                BankName = bankName,
                AccountNumber = accountNumber,
                AccountHolderName = accountHolderName,
                User = new User { Uuid = userUuid, Username = "owner", PasswordHash = "x" },
                Event = new Event { Uuid = eventUuid, Name = "Đà Lạt" }
            };
            Links.Add(link);
            return Task.FromResult(link);
        }

        public Task<EventShareLink?> GetActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            var now = AppDateTime.Now;
            var active = Links
                .Where(link => link.User.Uuid == userUuid && link.Event.Uuid == eventUuid && link.RevokedAt == null && link.ExpiresAt > now)
                .OrderByDescending(link => link.CreatedAt)
                .FirstOrDefault();
            return Task.FromResult(active);
        }

        public Task<(bool Revoked, string? Token)> RevokeActiveByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            var now = AppDateTime.Now;
            var active = Links
                .Where(link => link.User.Uuid == userUuid && link.Event.Uuid == eventUuid && link.RevokedAt == null && link.ExpiresAt > now)
                .OrderByDescending(link => link.CreatedAt)
                .FirstOrDefault();
            if (active is null)
                return Task.FromResult((false, (string?)null));

            active.RevokedAt = now;
            return Task.FromResult((true, (string?)active.Token));
        }

        public Task<EventShareLink?> GetByTokenAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult(Links.FirstOrDefault(link => link.Token == token));

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeStatsService : IStatsService
    {
        public EventBalanceResponse? Balance { get; set; }
        public bool ThrowNotFound { get; set; }
        public string? LastUserUuid { get; private set; }

        public Task<EventBalanceResponse> GetEventBalanceAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            LastUserUuid = userUuid;
            if (ThrowNotFound)
                throw new ErrorException(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
            return Task.FromResult(Balance!);
        }

        public Task<OverviewStatsResponse> GetOverviewAsync(string userUuid, StatsRangeRequest range, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ByCategoryStatsResponse> GetByCategoryAsync(string userUuid, ByCategoryStatsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeEventsService : IEventsService
    {
        public EventResponse? Event { get; set; }
        public bool ThrowNotFound { get; set; }
        public string? LastUserUuid { get; private set; }

        public Task<EventResponse> GetAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            LastUserUuid = userUuid;
            if (ThrowNotFound)
                throw new ErrorException(ErrorCodes.EventNotFound, "Không tìm thấy đợt chi tiêu.");
            return Task.FromResult(Event!);
        }

        public Task<IReadOnlyList<EventSummaryResponse>> ListAsync(string userUuid, EventFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventResponse> CreateAsync(string userUuid, CreateEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EventResponse> UpdateAsync(string userUuid, string eventUuid, UpdateEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CloseAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetMemberSettledAsync(string userUuid, string eventUuid, string memberUuid, SetSettledRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeExpensesService : IExpensesService
    {
        public IReadOnlyList<ExpenseResponse> Detailed { get; set; } = [];
        public string? LastUserUuid { get; private set; }

        public Task<IReadOnlyList<ExpenseResponse>> ListDetailedByEventAsync(string userUuid, string eventUuid, CancellationToken cancellationToken = default)
        {
            LastUserUuid = userUuid;
            return Task.FromResult(Detailed);
        }

        public Task<ExpenseResponse> GetAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExpenseSummaryResponse>> ListAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> CreateAsync(string userUuid, CreateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> UpdateAsync(string userUuid, string expenseUuid, UpdateExpenseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSettledAsync(string userUuid, string expenseUuid, SetSettledRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExpenseResponse> AssignEventAsync(string userUuid, string expenseUuid, AssignEventRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveEventAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditLogResponse>> GetHistoryAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private sealed class FakeWalletQrService : IWalletQrService
    {
        public IReadOnlyList<MemberQrResponse> ForShareResult { get; set; } = [];
        public int ForShareCallCount { get; private set; }
        public string? LastOwnerUuid { get; private set; }
        public string? LastEventUuid { get; private set; }
        public BankSnapshot? LastSnapshot { get; private set; }

        public Task<IReadOnlyList<MemberQrResponse>> GenerateEventMemberQrsForShareAsync(string ownerUserUuid, string eventUuid, BankSnapshot bankSnapshot, CancellationToken cancellationToken = default)
        {
            ForShareCallCount++;
            LastOwnerUuid = ownerUserUuid;
            LastEventUuid = eventUuid;
            LastSnapshot = bankSnapshot;
            return Task.FromResult(ForShareResult);
        }

        public Task<QrImageResult> GenerateExpenseQrAsync(string userUuid, string expenseUuid, string? bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<QrImageResult> GenerateEventQrAsync(string userUuid, string eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MemberQrResponse>> GenerateExpenseMemberQrsAsync(string userUuid, string expenseUuid, string? bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MemberQrResponse>> GenerateEventMemberQrsAsync(string userUuid, string eventUuid, string? bankAccountUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
