using FairShareMonApi.Services.Api.Wallet;
using SkiaSharp;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for <see cref="QrImageService"/> (M9 OQ2/OQ3 + the QR-image-header feature, no DB).
/// Proves both renderers emit a PNG (SkiaSharp, magic bytes 89 50 4E 47) at the baseline <c>BaseImageWidth</c>
/// (380px) for short header fields, that the width GROWS (up to <c>MaxImageWidth</c> 1600px) to keep the
/// widest bank field intact, that a header band is always drawn above the QR (so the image is strictly
/// taller than a bare <c>QrSize</c>(280) square), that the expense amount line adds height, that the
/// composite grows with more members, and that Vietnamese header/label text renders without throwing (the
/// bundled Be Vietnam Pro font is loaded). Per-member composition count is asserted structurally in
/// <c>WalletQrServiceTests</c>; pixel-level QR counting isn't attempted here (brittle).
/// </summary>
public class QrImageServiceTests
{
    // PNG file signature: 89 50 4E 47 0D 0A 1A 0A.
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    // Mirrors the private layout constants in QrImageService: the baseline canvas width (short fields), the
    // hard upper cap the width clamps to, and the bare QR square.
    private const int ImageWidth = 380;   // == BaseImageWidth
    private const int MaxImageWidth = 1600;
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

    // ---------------------------------------------------------------------------------------------------
    // Dynamic width: the bank fields (bank name, account holder, account number, and — expense only —
    // amount) are rendered INTACT and the image WIDTH grows from BaseImageWidth(380) to fit the widest of
    // them, clamped to MaxImageWidth(1600). The title never drives width (it wraps within the resolved
    // width). These prove growth relationally — exact pixel widths depend on the embedded font's metrics
    // (the field paint's typeface is loaded from a private embedded resource and isn't reproducible here),
    // so we assert monotonic relationships and the hard clamp bounds rather than measured pixel counts.
    // ---------------------------------------------------------------------------------------------------

    // A short holder name that keeps every field comfortably inside the 332px baseline text width.
    private const string ShortHolderName = "Nguyễn Văn A";

    // A long (~70-char) Vietnamese full name: "Chủ tài khoản: {name}" cannot fit the 332px baseline text
    // width at HeaderFieldTextSize(22f), so the image must widen to keep it intact.
    private const string LongHolderName =
        "Nguyễn Hoàng Gia Bảo Phạm Trần Lê Minh Phương Đặng Thị Ngọc Ánh Dương Quốc Cường";

    // A long bank name (not the holder) — proves the WIDEST of ALL bank fields drives width, not just holder.
    private const string LongBankName =
        "Ngân hàng Thương mại Cổ phần Đầu tư và Phát triển Việt Nam chi nhánh Thành phố Hồ Chí Minh";

    private static int RenderedWidth(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.Width;
    }

    [Fact]
    public void RenderSingle_LongAccountHolder_GrowsWidthWithinCap()
    {
        // A long account-holder name must widen the image beyond the 380 baseline (kept intact) but never
        // past the 1600 safety cap.
        var bytes = _service.RenderSingle(SamplePayload, SampleHeader() with { AccountHolderName = LongHolderName });

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.True(bitmap.Width > ImageWidth,
            $"a long holder name should grow the width beyond the {ImageWidth} baseline (got {bitmap.Width})");
        Assert.True(bitmap.Width <= MaxImageWidth,
            $"width ({bitmap.Width}) must stay within the {MaxImageWidth} cap");
        Assert.True(bitmap.Height > QrSize, "header band still sits above the QR");
    }

    [Fact]
    public void RenderSingle_LongerHolderName_ProducesStrictlyWiderImageThanShort()
    {
        // Monotonic growth: the ONLY difference between the two headers is the holder-name length, so a
        // strictly wider image proves the field is rendered intact (not truncated to a fixed width). Both
        // must render without throwing.
        byte[] shortBytes = [], longBytes = [];
        var exception = Record.Exception(() =>
        {
            shortBytes = _service.RenderSingle(SamplePayload, SampleHeader() with { AccountHolderName = ShortHolderName });
            longBytes = _service.RenderSingle(SamplePayload, SampleHeader() with { AccountHolderName = LongHolderName });
        });
        Assert.Null(exception);

        var shortWidth = RenderedWidth(shortBytes);
        var longWidth = RenderedWidth(longBytes);

        Assert.Equal(ImageWidth, shortWidth); // short fields stay at the baseline
        Assert.True(longWidth > shortWidth,
            $"a longer holder name ({longWidth}) must yield a strictly wider image than a short one ({shortWidth})");
    }

    [Fact]
    public void RenderSingle_LongBankName_GrowsWidth()
    {
        // Width is driven by the widest of ALL bank fields: a long BANK NAME (holder/number short) must
        // still grow the image, proving the growth isn't holder-specific.
        var shortWidth = RenderedWidth(_service.RenderSingle(SamplePayload, SampleHeader()));
        var longBankWidth = RenderedWidth(_service.RenderSingle(SamplePayload, SampleHeader() with { BankName = LongBankName }));

        Assert.Equal(ImageWidth, shortWidth);
        Assert.True(longBankWidth > ImageWidth,
            $"a long bank name should grow the width beyond {ImageWidth} (got {longBankWidth})");
        Assert.True(longBankWidth <= MaxImageWidth, $"width ({longBankWidth}) must stay within the {MaxImageWidth} cap");
    }

    [Fact]
    public void RenderSingle_ShortFields_StayAtBaselineWidth()
    {
        // Explicit guard: with all-short fields (both the no-amount and with-amount headers) the width does
        // NOT grow — it stays at the 380 baseline. Complements the existing Width==380 assertions.
        Assert.Equal(ImageWidth, RenderedWidth(_service.RenderSingle(SamplePayload, SampleHeader())));
        Assert.Equal(ImageWidth, RenderedWidth(_service.RenderSingle(SamplePayload, SampleHeaderWithAmount())));
    }

    [Fact]
    public void RenderSingle_PathologicallyLongField_CapsAtMaxWidthAndRenders()
    {
        // Safety valve: a field far wider than the cap (well beyond the ≤100-char validation bound) clamps
        // the width to exactly MaxImageWidth(1600) and still renders (the field is then ellipsized to fit).
        // Deterministic because the clamp target is a hard constant, not a font measurement.
        var absurdName = string.Concat(Enumerable.Repeat("Nguyễn Văn ", 40)); // ~440 chars, way over the cap
        byte[] bytes = [];
        var exception = Record.Exception(() =>
            bytes = _service.RenderSingle(SamplePayload, SampleHeader() with { AccountHolderName = absurdName }));

        Assert.Null(exception);
        Assert.True(StartsWithPngMagic(bytes));
        Assert.Equal(MaxImageWidth, RenderedWidth(bytes));
    }

    [Fact]
    public void RenderComposite_LongBankField_GrowsWidthAndRenders()
    {
        // The shared header band widens the composite canvas too; the QR + member labels must still center
        // on the wider canvas and render without throwing.
        var items = new[] { new QrCompositeItem("Nguyễn Văn A: 500.000đ", SamplePayload) };
        var shortWidth = RenderedWidth(_service.RenderComposite(items, SampleHeader()));

        byte[] longBytes = [];
        var exception = Record.Exception(() =>
            longBytes = _service.RenderComposite(items, SampleHeader() with { BankName = LongBankName }));
        Assert.Null(exception);

        Assert.True(StartsWithPngMagic(longBytes), "widened composite is still a PNG");
        var longWidth = RenderedWidth(longBytes);

        Assert.Equal(ImageWidth, shortWidth);
        Assert.True(longWidth > ImageWidth,
            $"a long bank field should grow the composite width beyond {ImageWidth} (got {longWidth})");
        Assert.True(longWidth <= MaxImageWidth, $"width ({longWidth}) must stay within the {MaxImageWidth} cap");
    }

    [Fact]
    public void RenderComposite_EmptyItems_Throws()
    {
        Assert.Throws<ArgumentException>(() => _service.RenderComposite([], SampleHeader()));
    }
}
