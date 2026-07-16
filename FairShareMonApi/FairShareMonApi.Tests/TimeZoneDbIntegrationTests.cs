using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Repositories;
using FairShareMonApi.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Integration tests for the timezone-aware DateTime FOUNDATION and M6 ranges against the real MariaDB
/// (skippable, per-test cleanup by username prefix). Covers:
/// <list type="bullet">
/// <item>Session pin: with <c>UtcSessionTimeZoneInterceptor</c> wired, an EF-owned connection reports
/// <c>@@session.time_zone = '+00:00'</c> and a DB-generated <c>UpdatedAt</c> reads back as UTC-now (not
/// shifted by a local session zone).</item>
/// <item>Read converter: entities loaded from the DB carry <c>Kind.Utc</c> on every DateTime.</item>
/// <item>M6 tz-aware ranges (D3): a whole-day range in a +7 zone stores the correct UTC bounds, and an
/// expense at the local-day edge is in range while one just outside is not.</item>
/// </list>
/// The M5 audit no-op regression (now fixed by the read converter) is verified by
/// <c>ExpenseRepositoryTests.UpdateGeneralInfoAsync_NoChange_WritesNoAuditRow</c>.
/// </summary>
[Collection("AuthIntegration")]
public class TimeZoneDbIntegrationTests(DatabaseFixture fixture) : ExpenseDbTestBase(fixture), IClassFixture<DatabaseFixture>
{
    private static readonly MariaDbServerVersion ServerVersion = new(new Version(11, 7, 2));

    // ---- Session pin (interceptor) ----------------------------------------------------------------

    [SkippableFact]
    public async Task SessionPin_InterceptorForcesUtcSession_AndDbGeneratedUpdatedAtIsUtc()
    {
        // A dedicated EF-owned context WITH the production interceptor. EF opens the connection itself, so
        // ConnectionOpenedAsync fires and runs SET time_zone='+00:00'. An EF transaction rolled back at the
        // end keeps the real DB clean (and the prefix sweep in DisposeAsync is a belt-and-braces backstop).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(Fixture.ConnectionString, ServerVersion)
            .AddInterceptors(new UtcSessionTimeZoneInterceptor())
            .Options;

        await using var context = new AppDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        // The interceptor pinned the session zone when the connection opened.
        Assert.Equal("+00:00", await ReadSessionTimeZoneAsync(context));

        var user = new User { Username = NewUsername(), PasswordHash = "seeded-hash-not-a-password" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var reloaded = await context.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        var utcNow = DateTime.UtcNow;

        Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedAt.Kind);
        var delta = (reloaded.UpdatedAt - utcNow).Duration();
        Assert.True(delta < TimeSpan.FromMinutes(10),
            $"DB-generated UpdatedAt {reloaded.UpdatedAt:o} is not ~UTC-now {utcNow:o} (delta {delta}); an unpinned/local session would be off by its offset (e.g. 7h for +7).");

        await transaction.RollbackAsync();
    }

    private static async Task<string> ReadSessionTimeZoneAsync(AppDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@session.time_zone";
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        var value = await command.ExecuteScalarAsync();
        return (string)value!;
    }

    // ---- Read converter: Kind.Utc -----------------------------------------------------------------

    [SkippableFact]
    public async Task ReadConverter_MaterializedDateTimes_CarryKindUtc()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await CreateEventRepository().CreateAsync(ledger.User.Uuid, new CreateEventData(
            "Đà Lạt", null,
            new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneInfo.Utc));
        var expense = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ăn tối", null, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc), null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], evt.Entity!.Uuid));

        var reloadedExpense = await ReloadExpenseAsync(expense.Entity!.Uuid);
        var reloadedEvent = await ReloadEventAsync(evt.Entity.Uuid);

        Assert.Equal(DateTimeKind.Utc, reloadedExpense!.ExpenseTime.Kind);
        Assert.Equal(DateTimeKind.Utc, reloadedExpense.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, reloadedExpense.UpdatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, reloadedEvent!.StartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, reloadedEvent.EndDate.Kind);
        Assert.Equal(DateTimeKind.Utc, reloadedEvent.CreatedAt.Kind);
    }

    // ---- M6 tz-aware ranges (D3) ------------------------------------------------------------------

    [SkippableFact]
    public async Task CreateEvent_WholeDayInPlus7Zone_StoresTzNormalizedUtcBounds()
    {
        var user = await SeedUserAsync();

        // Any instant inside 14/07 (+7): 03:00Z == 10:00 on 14/07 in +7.
        var result = await CreateEventRepository().CreateAsync(user.Uuid, new CreateEventData(
            "Đà Lạt", null,
            new DateTime(2026, 7, 14, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 14, 3, 0, 0, DateTimeKind.Utc),
            TestTimeZones.Plus7));

        Assert.Equal(EventWriteStatus.Success, result.Status);
        var persisted = await ReloadEventAsync(result.Entity!.Uuid);
        // Whole day 14/07 in +7 -> UTC bounds [13/07 17:00:00, 14/07 16:59:59.999999].
        Assert.Equal(new DateTime(2026, 7, 13, 17, 0, 0, DateTimeKind.Utc), persisted!.StartDate);
        Assert.Equal(new DateTime(2026, 7, 14, 17, 0, 0, DateTimeKind.Utc).AddTicks(-10), persisted.EndDate);
    }

    [SkippableFact]
    public async Task ExpenseAtLocalDayEdge_InPlus7Event_IsInRange_JustOutsideIsNot()
    {
        var ledger = await SeedLedgerAsync();
        var evt = await CreateEventRepository().CreateAsync(ledger.User.Uuid, new CreateEventData(
            "Đà Lạt", null,
            new DateTime(2026, 7, 14, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 14, 3, 0, 0, DateTimeKind.Utc),
            TestTimeZones.Plus7));
        Assert.Equal(EventWriteStatus.Success, evt.Status);

        // 00:30 on 14/07 in +7 == 13/07 17:30Z -> inside the [13/07 17:00Z, ...] window.
        var inRange = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Trong khoảng", null, new DateTime(2026, 7, 13, 17, 30, 0, DateTimeKind.Utc), null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], null));
        var assignIn = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, inRange.Entity!.Uuid, evt.Entity!.Uuid);
        Assert.Equal(ExpenseWriteStatus.Success, assignIn.Status);

        // 23:30 on 13/07 in +7 == 13/07 16:30Z -> before the start bound 17:00Z -> out of range.
        var outOfRange = await CreateExpenseRepository().CreateAsync(ledger.User.Uuid, new CreateExpenseData(
            "Ngoài khoảng", null, new DateTime(2026, 7, 13, 16, 30, 0, DateTimeKind.Utc), null, null, [],
            [new CreateShareData(ledger.OwnerRep.Uuid, 100_000m, null)], null));
        var assignOut = await CreateExpenseRepository().AssignEventAsync(ledger.User.Uuid, outOfRange.Entity!.Uuid, evt.Entity.Uuid);
        Assert.Equal(ExpenseWriteStatus.ExpenseTimeOutOfEventRange, assignOut.Status);
    }
}
