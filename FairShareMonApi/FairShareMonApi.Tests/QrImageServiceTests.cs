using FairShareMonApi.Services.Api.Wallet;
using SkiaSharp;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="QrImageService"/> (M9 OQ2/OQ3 + the QR-image-header feature, no DB).
/// Proves both renderers emit a PNG (SkiaSharp, magic bytes 89 50 4E 47) at the fixed <c>ImageWidth</c>
/// (380px), that a header band is always drawn above the QR (so the image is strictly taller than a bare
/// <c>QrSize</c>(280) square), that the expense amount line adds height, that the composite grows with more
/// members, and that Vietnamese header/label text renders without throwing (the bundled Be Vietnam Pro font
/// is loaded). Per-member composition count is asserted structurally in <c>WalletQrServiceTests</c>;
/// pixel-level QR counting isn't attempted here (brittle).
/// </summary>
public class QrImageServiceTests
{
    // PNG file signature: 89 50 4E 47 0D 0A 1A 0A.
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    // Mirrors the private layout constants in QrImageService: fixed canvas width and the bare QR square.
    private const int ImageWidth = 380;
    private const int QrSize = 280;
    // Mirrors QrImageService.TitleLineHeight: each wrapped title line adds this many device pixels to the band.
    private const int TitleLineHeight = 40;

    private readonly QrImageService _service = new();

    private const string SamplePayload =
        "00020101021238540010A00000072701240006970436011012345678900208QRIBFTTA53037045802VN6304ABCD";

    // Event-style header: no amount line (per-member amounts live under each QR).
    private static QrHeader SampleHeader() =>
        new(
            Title: "Chuyến đi Đà Lạt",
            BankLabel: "Ngân hàng",
            BankName: "Vietcombank",
            HolderLabel: "Chủ tài khoản",
            AccountHolderName: "Nguyễn Văn A",
            NumberLabel: "Số tài khoản",
            AccountNumber: "0123456789",
            AmountLabel: null,
            AmountText: null);

    // Expense-style header: carries the amount line.
    private static QrHeader SampleHeaderWithAmount() =>
        SampleHeader() with { AmountLabel = "Số tiền", AmountText = "500.000đ" };

    private static bool StartsWithPngMagic(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == PngMagic[0] && bytes[1] == PngMagic[1] && bytes[2] == PngMagic[2] && bytes[3] == PngMagic[3];

    [Fact]
    public void RenderSingle_ReturnsNonEmptyPngBytes()
    {
        var bytes = _service.RenderSingle(SamplePayload, SampleHeader());

        Assert.NotEmpty(bytes);
        Assert.True(StartsWithPngMagic(bytes), "single QR must be a PNG (magic 89 50 4E 47)");
    }

    [Fact]
    public void RenderSingle_HasFixedWidthAndHeaderMakesItTallerThanBareQr()
    {
        var bytes = _service.RenderSingle(SamplePayload, SampleHeader());

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(ImageWidth, bitmap.Width);
        // The header band always sits above the QrSize(280) square, so the image is strictly taller than a
        // bare QR would be.
        Assert.True(bitmap.Height > QrSize, $"single QR height ({bitmap.Height}) must exceed the bare QrSize baseline ({QrSize})");
    }

    [Fact]
    public void RenderSingle_ExpenseHeaderWithAmount_IsTallerThanSameHeaderWithoutAmount()
    {
        var withoutAmount = _service.RenderSingle(SamplePayload, SampleHeader());
        var withAmount = _service.RenderSingle(SamplePayload, SampleHeaderWithAmount());

        using var noAmountBitmap = SKBitmap.Decode(withoutAmount);
        using var amountBitmap = SKBitmap.Decode(withAmount);

        Assert.Equal(ImageWidth, amountBitmap.Width);
        // The extra "Số tiền: ..." field line adds one HeaderFieldLineHeight to the band.
        Assert.True(amountBitmap.Height > noAmountBitmap.Height,
            $"header with amount ({amountBitmap.Height}) should be taller than without ({noAmountBitmap.Height})");
    }

    [Fact]
    public void RenderSingle_VietnameseHeader_RendersWithoutThrowing()
    {
        // The embedded font must handle the full diacritic set in the header title + field values.
        var exception = Record.Exception(() => _service.RenderSingle(SamplePayload, SampleHeaderWithAmount()));

        Assert.Null(exception);
    }

    // A short, single-word-pair title that fits comfortably on one title line at TitleTextSize (30f) within
    // the header text width (ImageWidth - 2*HeaderPadding = 332px).
    private const string ShortTitle = "Ăn trưa";

    // A long multi-word Vietnamese expense name that cannot fit on one title line and therefore wraps to a
    // second (~49 chars, well over the ~20-char single-line capacity at 30f).
    private const string LongTwoLineTitle = "Tiệc tất niên công ty toàn thể nhân viên cuối năm";

    // A title far longer than the 2-line cap can hold (would need many lines); the renderer must cap it at
    // MaxTitleLines(2) with the tail ellipsised, never grow the band nor drop the tail silently.
    private const string ExtremeOverflowTitle =
        "Chuyến du lịch nghỉ dưỡng cuối năm của toàn thể phòng ban kỹ thuật và phòng kinh doanh tại khu resort ven biển Nha Trang Khánh Hòa";

    private static int RenderedHeight(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.Height;
    }

    [Fact]
    public void RenderSingle_LongTitle_WrapsToSecondLine_ProducingTallerImage()
    {
        // Regression: WrapText's final line must carry the full unconsumed tail (fix for the dropped
        // multi-line-tail bug), so a long title genuinely wraps to a second line and the header band grows
        // by exactly one TitleLineHeight. Both headers are identical except the title (same 3 bank fields,
        // no amount), so the height delta is purely the extra title line.
        var shortHeight = RenderedHeight(_service.RenderSingle(SamplePayload, SampleHeader() with { Title = ShortTitle }));
        var longHeight = RenderedHeight(_service.RenderSingle(SamplePayload, SampleHeader() with { Title = LongTwoLineTitle }));

        Assert.True(longHeight > shortHeight,
            $"long two-line title ({longHeight}) must produce a taller image than a one-line title ({shortHeight})");
        Assert.Equal(TitleLineHeight, longHeight - shortHeight);
    }

    [Fact]
    public void RenderSingle_TitleExceedingTwoLineCap_CapsHeightAndRendersWithoutThrowing()
    {
        // Regression + cap: an over-long title must still render (no throw) and the band must cap at exactly
        // MaxTitleLines(2). Proven at the layout level: the extreme title's band is one TitleLineHeight taller
        // than a one-line title (i.e. exactly 2 title lines, not 3+), so the wrapped tail is neither dropped
        // as extra lines nor allowed to overflow the cap. (Pixel-level ellipsis "…" on the last line is not
        // asserted — WrapText is private and there is no non-pixel seam; see the coverage note.)
        var exception = Record.Exception(() => _service.RenderSingle(SamplePayload, SampleHeader() with { Title = ExtremeOverflowTitle }));
        Assert.Null(exception);

        var shortHeight = RenderedHeight(_service.RenderSingle(SamplePayload, SampleHeader() with { Title = ShortTitle }));
        var extremeHeight = RenderedHeight(_service.RenderSingle(SamplePayload, SampleHeader() with { Title = ExtremeOverflowTitle }));
        var twoLineHeight = RenderedHeight(_service.RenderSingle(SamplePayload, SampleHeader() with { Title = LongTwoLineTitle }));

        Assert.True(extremeHeight > shortHeight,
            $"an over-long title ({extremeHeight}) must use more than one title line vs a one-line title ({shortHeight})");
        // Capped at exactly 2 lines: same height as a title that needs exactly 2 lines, and exactly one
        // TitleLineHeight above the one-line baseline (never 3+ lines).
        Assert.Equal(twoLineHeight, extremeHeight);
        Assert.Equal(TitleLineHeight, extremeHeight - shortHeight);
    }

    [Fact]
    public void RenderComposite_LongTitle_WrapsToSecondLine_ProducingTallerImage()
    {
        // The header band is shared by both renderers; confirm the composite header wraps the title too
        // (extra coverage beyond the planning doc's two RenderSingle cases).
        var items = new[] { new QrCompositeItem("Nguyễn Văn A: 500.000đ", SamplePayload) };
        var shortHeight = RenderedHeight(_service.RenderComposite(items, SampleHeader() with { Title = ShortTitle }));
        var longHeight = RenderedHeight(_service.RenderComposite(items, SampleHeader() with { Title = ExtremeOverflowTitle }));

        Assert.True(longHeight > shortHeight,
            $"composite long title ({longHeight}) must produce a taller image than a one-line title ({shortHeight})");
        Assert.Equal(TitleLineHeight, longHeight - shortHeight);
    }

    [Fact]
    public void RenderComposite_SingleItem_ReturnsPng()
    {
        var bytes = _service.RenderComposite([new QrCompositeItem("Nguyễn Văn A: 500.000đ", SamplePayload)], SampleHeader());

        Assert.NotEmpty(bytes);
        Assert.True(StartsWithPngMagic(bytes), "composite must be a PNG (OQ3b)");
    }

    [Fact]
    public void RenderComposite_HasFixedWidthAndHeaderMakesItTallerThanBareQr()
    {
        var bytes = _service.RenderComposite([new QrCompositeItem("Nguyễn Văn A: 500.000đ", SamplePayload)], SampleHeader());

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(ImageWidth, bitmap.Width);
        Assert.True(bitmap.Height > QrSize, $"composite height ({bitmap.Height}) must exceed the bare QrSize baseline ({QrSize})");
    }

    [Fact]
    public void RenderComposite_VietnameseLabels_RenderWithoutThrowing()
    {
        // The embedded font must handle the full diacritic set (headless-safe).
        var items = new[]
        {
            new QrCompositeItem("Nguyễn Văn A: 500.000đ", SamplePayload),
            new QrCompositeItem("Trần Thị Bích Đào: 1.250.000đ", SamplePayload),
            new QrCompositeItem("Lê Cường: 75.000đ", SamplePayload)
        };

        var bytes = _service.RenderComposite(items, SampleHeader());

        Assert.True(StartsWithPngMagic(bytes));
    }

    [Fact]
    public void RenderComposite_MoreMembers_ProducesTallerLargerImage()
    {
        var one = _service.RenderComposite([new QrCompositeItem("A: 1đ", SamplePayload)], SampleHeader());
        var three = _service.RenderComposite(
        [
            new QrCompositeItem("A: 1đ", SamplePayload),
            new QrCompositeItem("B: 2đ", SamplePayload),
            new QrCompositeItem("C: 3đ", SamplePayload)
        ], SampleHeader());

        // Vertical stacking: three cells encode to strictly more bytes than one.
        Assert.True(three.Length > one.Length, $"three-member composite ({three.Length}) should exceed one-member ({one.Length})");

        // ...and the decoded canvas is strictly taller (same header band, three cells vs one).
        using var oneBitmap = SKBitmap.Decode(one);
        using var threeBitmap = SKBitmap.Decode(three);
        Assert.True(threeBitmap.Height > oneBitmap.Height,
            $"three-member composite height ({threeBitmap.Height}) should exceed one-member ({oneBitmap.Height})");
    }

    [Fact]
    public void RenderComposite_EmptyItems_Throws()
    {
        Assert.Throws<ArgumentException>(() => _service.RenderComposite([], SampleHeader()));
    }
}
