using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Expenses;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Expenses;
using FairShareMonApi.Validators.Expenses;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>ExpensesService</c> over fake <see cref="IExpenseRepository"/> /
/// <see cref="IAuditLogRepository"/> plus the real AutoMapper profiles and real validators (no DB).
/// Proves: create maps the repo's typed write result to 6001/6002/6003/7001/7003; a validation failure
/// throws before touching the repo; get/update/delete/settled misses → 6000; a no-op update (repo
/// Success) throws nothing; history maps rows and returns an empty list when none.
/// </summary>
public class ExpensesServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-0000000000e5";

    private readonly FakeExpenseRepository _expenses = new();
    private readonly FakeAuditLogRepository _audit = new();

    private readonly IMapper _mapper = new MapperConfiguration(config =>
    {
        config.AddProfile<ExpenseProfile>();
        config.AddProfile<ShareProfile>();
        config.AddProfile<MemberProfile>();
        config.AddProfile<CategoryProfile>();
        config.AddProfile<TagProfile>();
        config.AddProfile<AuditLogProfile>();
    }).CreateMapper();

    private ExpensesService CreateService() =>
        new(_expenses, _audit, _mapper, new CreateExpenseRequestValidator(), new UpdateExpenseRequestValidator());

    private static CreateExpenseRequest CreateRequest() =>
        new()
        {
            Name = "Ăn trưa",
            ExpenseTime = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc),
            Shares = [new CreateShareInput { MemberUuid = "m-1", Amount = 100_000m }]
        };

    private static UpdateExpenseRequest UpdateRequest() =>
        new() { Name = "Ăn tối", ExpenseTime = new DateTime(2026, 7, 14, 20, 0, 0, DateTimeKind.Utc) };

    private static Expense FullExpense()
    {
        var payer = new Member { Name = "Chủ sổ", IsOwnerRepresentative = true };
        var category = new Category { Name = "Ăn uống", Color = "#F97316", IsDefault = true };
        var expense = new Expense
        {
            Name = "Ăn trưa",
            ExpenseTime = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc),
            PayerMember = payer,
            Category = category
        };
        expense.Shares.Add(new Share { Amount = 60_000m, Member = payer });
        expense.Shares.Add(new Share { Amount = 40_000m, Member = new Member { Name = "An" } });
        return expense;
    }

    [Fact]
    public async Task CreateAsync_Success_ReturnsMappedResponseWithDerivedTotal()
    {
        var expense = FullExpense();
        _expenses.StoredExpense = expense;
        _expenses.CreateResult = ExpenseWriteResult<Expense>.Success(expense);

        var response = await CreateService().CreateAsync(UserUuid, CreateRequest());

        Assert.Equal(expense.Uuid, response.Uuid);
        Assert.Equal(100_000m, response.Total); // derived from shares (OQ1)
        Assert.Equal(2, response.Shares.Count);
        Assert.Equal("Ăn uống", response.Category.Name);
        Assert.Equal("Chủ sổ", response.Payer.Name);
    }

    [Fact]
    public async Task CreateAsync_TrimsNameAndPassesDataToRepository()
    {
        var expense = FullExpense();
        _expenses.StoredExpense = expense;
        _expenses.CreateResult = ExpenseWriteResult<Expense>.Success(expense);

        var request = CreateRequest();
        request.Name = "   Ăn trưa   ";
        await CreateService().CreateAsync(UserUuid, request);

        Assert.Equal("Ăn trưa", _expenses.LastCreateData!.Name);
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_ThrowsValidationExceptionAndSkipsRepository()
    {
        var request = CreateRequest();
        request.Name = "";

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().CreateAsync(UserUuid, request));

        Assert.Null(_expenses.LastCreateData); // repo never called
    }

    [Theory]
    [InlineData(ExpenseWriteStatus.PayerInvalid, ErrorCodes.ExpensePayerInvalid)]
    [InlineData(ExpenseWriteStatus.CategoryInvalid, ErrorCodes.ExpenseCategoryInvalid)]
    [InlineData(ExpenseWriteStatus.TagInvalid, ErrorCodes.ExpenseTagInvalid)]
    [InlineData(ExpenseWriteStatus.ShareMemberInvalid, ErrorCodes.ShareMemberInvalid)]
    [InlineData(ExpenseWriteStatus.DuplicateShareMember, ErrorCodes.DuplicateShareMember)]
    [InlineData(ExpenseWriteStatus.ExpenseNotFound, ErrorCodes.ExpenseNotFound)]
    public async Task CreateAsync_RepositoryFailure_MapsToErrorCode(ExpenseWriteStatus status, int expectedCode)
    {
        _expenses.CreateResult = ExpenseWriteResult<Expense>.Fail(status);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().CreateAsync(UserUuid, CreateRequest()));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Miss_ThrowsExpenseNotFound6000()
    {
        _expenses.StoredExpense = null;

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().GetAsync(UserUuid, "no-such"));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    [Fact]
    public async Task GetAsync_Found_ReturnsMappedResponse()
    {
        var expense = FullExpense();
        _expenses.StoredExpense = expense;

        var response = await CreateService().GetAsync(UserUuid, expense.Uuid);

        Assert.Equal(expense.Uuid, response.Uuid);
        Assert.Equal(100_000m, response.Total);
    }

    [Fact]
    public async Task UpdateAsync_Miss_ThrowsExpenseNotFound6000()
    {
        _expenses.UpdateStatus = ExpenseWriteStatus.ExpenseNotFound;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "no-such", UpdateRequest()));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    [Fact]
    public async Task UpdateAsync_Success_ReturnsResponseAndThrowsNothing()
    {
        var expense = FullExpense();
        _expenses.StoredExpense = expense;
        _expenses.UpdateStatus = ExpenseWriteStatus.Success; // a no-op update still commits general info, no error

        var response = await CreateService().UpdateAsync(UserUuid, expense.Uuid, UpdateRequest());

        Assert.Equal(expense.Uuid, response.Uuid);
    }

    [Theory]
    [InlineData(ExpenseWriteStatus.PayerInvalid, ErrorCodes.ExpensePayerInvalid)]
    [InlineData(ExpenseWriteStatus.CategoryInvalid, ErrorCodes.ExpenseCategoryInvalid)]
    [InlineData(ExpenseWriteStatus.TagInvalid, ErrorCodes.ExpenseTagInvalid)]
    public async Task UpdateAsync_LinkInvalid_MapsToErrorCode(ExpenseWriteStatus status, int expectedCode)
    {
        _expenses.UpdateStatus = status;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "e-1", UpdateRequest()));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_Miss_ThrowsExpenseNotFound6000()
    {
        _expenses.DeleteStatus = ExpenseWriteStatus.ExpenseNotFound;

        var exception = await Assert.ThrowsAsync<ErrorException>(() => CreateService().DeleteAsync(UserUuid, "no-such"));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_Success_ThrowsNothing()
    {
        _expenses.DeleteStatus = ExpenseWriteStatus.Success;

        await CreateService().DeleteAsync(UserUuid, "e-1");
    }

    [Fact]
    public async Task SetSettledAsync_Miss_ThrowsExpenseNotFound6000()
    {
        _expenses.SetSettledStatus = ExpenseWriteStatus.ExpenseNotFound;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().SetSettledAsync(UserUuid, "no-such", new SetSettledRequest { IsSettled = true }));

        Assert.Equal(ErrorCodes.ExpenseNotFound, exception.Code);
    }

    [Fact]
    public async Task GetHistoryAsync_MapsRowsToResponses()
    {
        _audit.Logs =
        [
            new AuditLog
            {
                ActorUserId = 1, EntityType = AuditEntityType.Expense, EntityUuid = "e-1", ExpenseUuid = "e-1",
                Action = AuditAction.Create, BeforeData = null, AfterData = "{\"name\":\"Ăn trưa\"}"
            },
            new AuditLog
            {
                ActorUserId = 1, EntityType = AuditEntityType.Share, EntityUuid = "s-1", ExpenseUuid = "e-1",
                Action = AuditAction.Delete, BeforeData = "{\"amount\":100000}", AfterData = null
            }
        ];

        var history = await CreateService().GetHistoryAsync(UserUuid, "e-1");

        Assert.Equal(2, history.Count);
        Assert.Equal("Expense", history[0].EntityType);
        Assert.Equal("Create", history[0].Action);
        Assert.Null(history[0].Before);
        Assert.NotNull(history[0].After);
        Assert.Equal("Share", history[1].EntityType);
        Assert.Equal("Delete", history[1].Action);
        Assert.NotNull(history[1].Before);
        Assert.Null(history[1].After);
    }

    [Fact]
    public async Task GetHistoryAsync_NoRows_ReturnsEmptyList()
    {
        _audit.Logs = [];

        var history = await CreateService().GetHistoryAsync(UserUuid, "unknown");

        Assert.Empty(history);
    }

    private sealed class FakeExpenseRepository : IExpenseRepository
    {
        public Expense? StoredExpense { get; set; }

        public ExpenseWriteResult<Expense> CreateResult { get; set; } = ExpenseWriteResult<Expense>.Fail(ExpenseWriteStatus.ExpenseNotFound);

        public ExpenseWriteStatus UpdateStatus { get; set; } = ExpenseWriteStatus.Success;

        public ExpenseWriteStatus DeleteStatus { get; set; } = ExpenseWriteStatus.Success;

        public ExpenseWriteStatus SetSettledStatus { get; set; } = ExpenseWriteStatus.Success;

        public CreateExpenseData? LastCreateData { get; private set; }

        public Task<IReadOnlyList<Expense>> ListByUserAsync(string userUuid, ExpenseFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Expense>>(StoredExpense is null ? [] : [StoredExpense]);

        public Task<Expense?> GetByUuidAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(StoredExpense);

        public Task<ExpenseWriteResult<Expense>> CreateAsync(string userUuid, CreateExpenseData data, CancellationToken cancellationToken = default)
        {
            LastCreateData = data;
            return Task.FromResult(CreateResult);
        }

        public Task<ExpenseWriteResult<Expense>> UpdateGeneralInfoAsync(string userUuid, string expenseUuid, UpdateExpenseData data, CancellationToken cancellationToken = default) =>
            Task.FromResult(UpdateStatus == ExpenseWriteStatus.Success
                ? ExpenseWriteResult<Expense>.Success(StoredExpense!)
                : ExpenseWriteResult<Expense>.Fail(UpdateStatus));

        public Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeleteStatus);

        public Task<ExpenseWriteStatus> SetSettledAsync(string userUuid, string expenseUuid, bool isSettled, CancellationToken cancellationToken = default) =>
            Task.FromResult(SetSettledStatus);

        public IQueryable<Expense> Query(bool tracking = false, bool includeDeleted = false) => throw new NotSupportedException();

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public IReadOnlyList<AuditLog> Logs { get; set; } = [];

        public Task<IReadOnlyList<AuditLog>> ListByExpenseAsync(string userUuid, string expenseUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(Logs);

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
