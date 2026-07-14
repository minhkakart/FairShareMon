using AutoMapper;
using DiDecoration.Attributes;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models.Wallet;
using FairShareMonApi.Repositories;
using FluentValidation;

namespace FairShareMonApi.Services.Api.Wallet;

/// <summary>
/// Business logic for the wallet (ví, The-ideal.md §3.10): list / get / create / update / set-default
/// / delete the current user's bank accounts, all resource-owned (an ownership miss -&gt;
/// <c>BankAccountNotFound</c> 12000, never 403). Enforces the single-default invariant (first account
/// auto-default, atomic swap on set-default, promote-another on delete-of-default) delegated to the
/// repository transaction. Bank accounts are hard-deleted (OQ7).
/// </summary>
public interface IBankAccountsService
{
    Task<IReadOnlyList<BankAccountResponse>> ListAsync(string userUuid, CancellationToken cancellationToken = default);

    Task<BankAccountResponse> GetAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default);

    Task<BankAccountResponse> CreateAsync(string userUuid, CreateBankAccountRequest request, CancellationToken cancellationToken = default);

    Task<BankAccountResponse> UpdateAsync(string userUuid, string bankAccountUuid, UpdateBankAccountRequest request, CancellationToken cancellationToken = default);

    Task SetDefaultAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default);
}

[ScopedService(typeof(IBankAccountsService))]
public sealed class BankAccountsService(
    IBankAccountRepository bankAccountRepository,
    IMapper mapper,
    IValidator<CreateBankAccountRequest> createValidator,
    IValidator<UpdateBankAccountRequest> updateValidator) : IBankAccountsService
{
    public async Task<IReadOnlyList<BankAccountResponse>> ListAsync(string userUuid, CancellationToken cancellationToken = default)
    {
        var accounts = await bankAccountRepository.ListByUserAsync(userUuid, cancellationToken);
        return mapper.Map<IReadOnlyList<BankAccountResponse>>(accounts);
    }

    public async Task<BankAccountResponse> GetAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default)
    {
        var account = await bankAccountRepository.GetByUuidAsync(userUuid, bankAccountUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<BankAccountResponse>(account);
    }

    public async Task<BankAccountResponse> CreateAsync(string userUuid, CreateBankAccountRequest request, CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var account = await bankAccountRepository.CreateAsync(
            userUuid,
            request.BankBin.Trim(),
            request.BankName.Trim(),
            request.AccountNumber.Trim(),
            request.AccountHolderName.Trim(),
            cancellationToken) ?? throw NotFound();

        return mapper.Map<BankAccountResponse>(account);
    }

    public async Task<BankAccountResponse> UpdateAsync(string userUuid, string bankAccountUuid, UpdateBankAccountRequest request, CancellationToken cancellationToken = default)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var updated = await bankAccountRepository.UpdateAsync(
            userUuid,
            bankAccountUuid,
            request.BankBin.Trim(),
            request.BankName.Trim(),
            request.AccountNumber.Trim(),
            request.AccountHolderName.Trim(),
            cancellationToken);
        if (!updated)
            throw NotFound();

        var account = await bankAccountRepository.GetByUuidAsync(userUuid, bankAccountUuid, cancellationToken)
            ?? throw NotFound();

        return mapper.Map<BankAccountResponse>(account);
    }

    public async Task SetDefaultAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default)
    {
        var updated = await bankAccountRepository.SetDefaultAsync(userUuid, bankAccountUuid, cancellationToken);
        if (!updated)
            throw NotFound();
    }

    public async Task DeleteAsync(string userUuid, string bankAccountUuid, CancellationToken cancellationToken = default)
    {
        var deleted = await bankAccountRepository.DeleteAsync(userUuid, bankAccountUuid, cancellationToken);
        if (!deleted)
            throw NotFound();
    }

    private static ErrorException NotFound() =>
        new(ErrorCodes.BankAccountNotFound, "Không tìm thấy tài khoản ngân hàng.");
}
