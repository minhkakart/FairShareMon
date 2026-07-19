using DiDecoration.Attributes;
using FairShareMonApi.Services.Api.Wallet;

namespace FairShareMonApi.Services.Api.Banks;

/// <summary>
/// Nhà cung cấp nội dung QR "local" (mặc định): adapter mỏng trên <see cref="IVietQrPayloadBuilder"/>,
/// cho ra chuỗi TLV giống hệt cách dựng tại chỗ hiện nay. <c>AccountHolderName</c> không được builder TLV
/// dùng đến.
/// </summary>
[ScopedService(typeof(IQrContentProvider), Multiple = true)]
public sealed class LocalQrContentProvider(IVietQrPayloadBuilder builder) : IQrContentProvider
{
    public string Key => "local";

    public Task<string> BuildContentAsync(QrContentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(builder.Build(request.BankBin, request.AccountNumber, request.Amount, request.AddInfo));
}
