using AutoMapper;
using FairShareMonApi.Constants;
using FairShareMonApi.Database;
using FairShareMonApi.Database.Entities;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Mappings;
using FairShareMonApi.Models.Shares;
using FairShareMonApi.Repositories;
using FairShareMonApi.Repositories.Abstractions;
using FairShareMonApi.Services.Api.Shares;
using FairShareMonApi.Validators.Shares;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <c>SharesService</c> over a fake <see cref="IShareRepository"/> plus the real
/// AutoMapper profiles and validators (no DB). Proves: add/update map the repo's typed write result to
/// 7001 (invalid member) / 7003 (duplicate) / 6000 (expense miss on add) / 7000 (share miss);
/// update change-member on the owner-rep and delete of the owner-rep share both → 7002; a validation
/// failure throws before touching the repo.
/// </summary>
public class SharesServiceTests
{
    private const string UserUuid = "0198a5c2-0000-7000-8000-0000000000f7";

    private readonly FakeShareRepository _shares = new();

    private readonly IMapper _mapper = new MapperConfiguration(config =>
    {
        config.AddProfile<ShareProfile>();
        config.AddProfile<MemberProfile>();
    }).CreateMapper();

    private SharesService CreateService() =>
        new(_shares, _mapper, new CreateShareRequestValidator(), new UpdateShareRequestValidator());

    private static CreateShareRequest CreateRequest(string memberUuid = "m-1", decimal amount = 50_000m) =>
        new() { MemberUuid = memberUuid, Amount = amount };

    private static UpdateShareRequest UpdateRequest(string memberUuid = "m-1", decimal amount = 50_000m) =>
        new() { MemberUuid = memberUuid, Amount = amount };

    private static Share StoredShare() =>
        new() { Amount = 50_000m, Member = new Member { Name = "An" } };

    [Fact]
    public async Task AddAsync_Success_ReturnsMappedShareResponse()
    {
        var share = StoredShare();
        _shares.AddResult = ExpenseWriteResult<Share>.Success(share);

        var response = await CreateService().AddAsync(UserUuid, "e-1", CreateRequest());

        Assert.Equal(share.Uuid, response.Uuid);
        Assert.Equal("An", response.Member.Name);
        Assert.Equal(50_000m, response.Amount);
    }

    [Fact]
    public async Task AddAsync_InvalidRequest_ThrowsValidationExceptionAndSkipsRepository()
    {
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            CreateService().AddAsync(UserUuid, "e-1", CreateRequest(amount: -1m)));

        Assert.Equal(0, _shares.AddCalls);
    }

    [Theory]
    [InlineData(ExpenseWriteStatus.ShareMemberInvalid, ErrorCodes.ShareMemberInvalid)]
    [InlineData(ExpenseWriteStatus.DuplicateShareMember, ErrorCodes.DuplicateShareMember)]
    [InlineData(ExpenseWriteStatus.ExpenseNotFound, ErrorCodes.ExpenseNotFound)]
    public async Task AddAsync_RepositoryFailure_MapsToErrorCode(ExpenseWriteStatus status, int expectedCode)
    {
        _shares.AddResult = ExpenseWriteResult<Share>.Fail(status);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().AddAsync(UserUuid, "e-1", CreateRequest()));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task UpdateAsync_Success_ReturnsMappedShareResponse()
    {
        var share = StoredShare();
        _shares.UpdateResult = ExpenseWriteResult<Share>.Success(share);

        var response = await CreateService().UpdateAsync(UserUuid, "e-1", share.Uuid, UpdateRequest());

        Assert.Equal(share.Uuid, response.Uuid);
    }

    [Theory]
    [InlineData(ExpenseWriteStatus.ShareMemberInvalid, ErrorCodes.ShareMemberInvalid)]
    [InlineData(ExpenseWriteStatus.DuplicateShareMember, ErrorCodes.DuplicateShareMember)]
    [InlineData(ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable, ErrorCodes.OwnerRepresentativeShareNotDeletable)]
    [InlineData(ExpenseWriteStatus.ShareNotFound, ErrorCodes.ShareNotFound)]
    public async Task UpdateAsync_RepositoryFailure_MapsToErrorCode(ExpenseWriteStatus status, int expectedCode)
    {
        _shares.UpdateResult = ExpenseWriteResult<Share>.Fail(status);

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().UpdateAsync(UserUuid, "e-1", "s-1", UpdateRequest()));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_OwnerRepShare_Throws7002()
    {
        _shares.DeleteStatus = ExpenseWriteStatus.OwnerRepresentativeShareNotDeletable;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DeleteAsync(UserUuid, "e-1", "s-1"));

        Assert.Equal(ErrorCodes.OwnerRepresentativeShareNotDeletable, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_Miss_ThrowsShareNotFound7000()
    {
        _shares.DeleteStatus = ExpenseWriteStatus.ShareNotFound;

        var exception = await Assert.ThrowsAsync<ErrorException>(() =>
            CreateService().DeleteAsync(UserUuid, "e-1", "no-such"));

        Assert.Equal(ErrorCodes.ShareNotFound, exception.Code);
    }

    [Fact]
    public async Task DeleteAsync_Success_ThrowsNothing()
    {
        _shares.DeleteStatus = ExpenseWriteStatus.Success;

        await CreateService().DeleteAsync(UserUuid, "e-1", "s-1");
    }

    private sealed class FakeShareRepository : IShareRepository
    {
        public ExpenseWriteResult<Share> AddResult { get; set; } = ExpenseWriteResult<Share>.Fail(ExpenseWriteStatus.ExpenseNotFound);

        public ExpenseWriteResult<Share> UpdateResult { get; set; } = ExpenseWriteResult<Share>.Fail(ExpenseWriteStatus.ShareNotFound);

        public ExpenseWriteStatus DeleteStatus { get; set; } = ExpenseWriteStatus.Success;

        public int AddCalls { get; private set; }

        public Task<ExpenseWriteResult<Share>> AddAsync(string userUuid, string expenseUuid, ShareData data, CancellationToken cancellationToken = default)
        {
            AddCalls++;
            return Task.FromResult(AddResult);
        }

        public Task<ExpenseWriteResult<Share>> UpdateAsync(string userUuid, string expenseUuid, string shareUuid, ShareData data, CancellationToken cancellationToken = default) =>
            Task.FromResult(UpdateResult);

        public Task<ExpenseWriteStatus> DeleteAsync(string userUuid, string expenseUuid, string shareUuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeleteStatus);

        public Task<TResult> ExecuteQueryAsync<TResult>(Func<AppDbContext, CancellationToken, Task<TResult>> query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TResult> ExecuteTransactionAsync<TResult>(Func<AppDbContext, TransactionContext, Task<TResult>> action, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
