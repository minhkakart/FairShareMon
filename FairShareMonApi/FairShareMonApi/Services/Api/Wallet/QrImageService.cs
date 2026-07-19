using System.Reflection;
using DiDecoration.Attributes;
using QRCoder;
using SkiaSharp;

namespace FairShareMonApi.Services.Api.Wallet;

/// <summary>
/// Renders VietQR payloads to images (OQ2/OQ3). Both public renderers draw a left-aligned text header
/// (destination bank info + a title, plus the amount on the expense header only) onto a SkiaSharp canvas.
/// <see cref="RenderSingle"/> produces the single-expense QR: the header band on top and one QR below.
/// <see cref="RenderComposite"/> produces the event QR: the header band once at the top, then one owing
/// member's QR plus a name + amount label per cell, vertically stacked into a single shareable image
/// (§3.10 "gửi vào nhóm chat"). The bare QRCoder PNG is produced by the private <c>RenderQrPng</c> helper
/// (no <c>System.Drawing</c>, cross-platform). Text uses a bundled SIL-OFL Vietnamese font (Be Vietnam Pro,
/// Regular weight only - no bold variant bundled) so diacritics render on headless Linux/Docker; if the
/// embedded font is unavailable it falls back to a system typeface that can render Vietnamese. The service
/// stays pure/synchronous - localization and directory lookups happen upstream in <c>WalletQrService</c>.
/// </summary>
public interface IQrImageService
{
    /// <summary>Renders a single VietQR payload to a PNG with a header band above the QR, via SkiaSharp.</summary>
    byte[] RenderSingle(string payload, QrHeader header);

    /// <summary>Renders a composite PNG: one header band on top, then one QR per item (member name + amount label under each), via SkiaSharp.</summary>
    byte[] RenderComposite(IReadOnlyList<QrCompositeItem> items, QrHeader header);
}

/// <summary>One entry of the event-QR composite: the human-readable label and its VietQR payload.</summary>
public sealed record QrCompositeItem(string Label, string Payload);

/// <summary>
/// The text header drawn above a QR image: an emphasized <paramref name="Title"/> (expense/event name)
/// and the destination bank fields as label+value pairs. <paramref name="AmountLabel"/> /
/// <paramref name="AmountText"/> are both non-null only on the expense header (the event header omits the
/// amount - per-member amounts stay under each member's QR); <paramref name="AmountText"/> is pre-formatted
/// VND. All label strings are already localized to the request culture by the caller.
/// </summary>
public sealed record QrHeader(
    string Title,
    string BankLabel,
    string BankName,
    string HolderLabel,
    string AccountHolderName,
    string NumberLabel,
    string AccountNumber,
    string? AmountLabel,
    string? AmountText);

[ScopedService(typeof(IQrImageService))]
public sealed class QrImageService : IQrImageService
{
    private const int SinglePixelsPerModule = 20;

    // Composite layout constants (device pixels).
    // The image width is the baseline unless the header's bank fields need more room: the destination
    // bank name / account holder / account number are always shown intact, so the width grows to fit
    // the widest of them (clamped to MaxImageWidth as a safety valve for pathological inputs).
    private const int BaseImageWidth = 380;
    private const int MaxImageWidth = 1600;
    private const int CellPadding = 24;
    private const int QrSize = 280;
    private const int LabelGap = 12;
    private const float LabelTextSize = 24f;
    private const float LabelLineHeight = 32f;
    private const int MaxLabelLines = 3;

    // Header layout constants (device pixels).
    private const int HeaderPadding = 24;
    private const float TitleTextSize = 30f;
    private const float TitleLineHeight = 40f;
    private const int MaxTitleLines = 2;
    private const float HeaderFieldTextSize = 22f;
    private const float HeaderFieldLineHeight = 30f;
    private const float HeaderTitleGap = 12f;
    private const float HeaderBottomGap = 16f;
    private const float DividerThickness = 2f;
    private static readonly SKColor DividerColor = new(0xE0, 0xE0, 0xE0);

    /// <summary>The bundled font resource name suffix (matches regardless of the assembly's default namespace prefix).</summary>
    private const string FontResourceSuffix = "BeVietnamPro-Regular.ttf";

    // Loaded once: embedded Be Vietnam Pro, else a Vietnamese-capable system fallback.
    private static readonly Lazy<SKTypeface> LabelTypeface = new(LoadLabelTypeface);

    public byte[] RenderSingle(string payload, QrHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var typeface = LabelTypeface.Value;
        using var titlePaint = CreateHeaderPaint(typeface, TitleTextSize);
        using var fieldPaint = CreateHeaderPaint(typeface, HeaderFieldTextSize);
        using var dividerPaint = new SKPaint { Color = DividerColor, StrokeWidth = DividerThickness, IsAntialias = true };

        var layout = BuildHeaderLayout(header, titlePaint, fieldPaint);

        // One QR cell below the band: CellPadding, the QrSize square, CellPadding (no label under the QR).
        var totalHeight = layout.Height + CellPadding + QrSize + CellPadding;

        using var qrBitmap = SKBitmap.Decode(RenderQrPng(payload));
        var info = new SKImageInfo(layout.Width, totalHeight);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        DrawHeaderBand(canvas, layout, titlePaint, fieldPaint, dividerPaint);

        var qrLeft = (layout.Width - QrSize) / 2f;
        var qrTop = layout.Height + CellPadding;
        var dest = new SKRect(qrLeft, qrTop, qrLeft + QrSize, qrTop + QrSize);
        canvas.DrawBitmap(qrBitmap, dest);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    public byte[] RenderComposite(IReadOnlyList<QrCompositeItem> items, QrHeader header)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(header);
        if (items.Count == 0)
            throw new ArgumentException("Cần ít nhất một mục để dựng ảnh QR tổng hợp.", nameof(items));

        var typeface = LabelTypeface.Value;
        using var labelPaint = new SKPaint
        {
            Typeface = typeface,
            TextSize = LabelTextSize,
            IsAntialias = true,
            Color = SKColors.Black,
            TextAlign = SKTextAlign.Center
        };
        using var titlePaint = CreateHeaderPaint(typeface, TitleTextSize);
        using var fieldPaint = CreateHeaderPaint(typeface, HeaderFieldTextSize);
        using var dividerPaint = new SKPaint { Color = DividerColor, StrokeWidth = DividerThickness, IsAntialias = true };

        // The header may widen the image to keep the bank fields intact; member labels wrap to that width.
        var layout = BuildHeaderLayout(header, titlePaint, fieldPaint);
        var maxTextWidth = layout.Width - (2 * CellPadding);

        // Pre-render each QR bitmap and wrap each label so total height is known before drawing.
        var qrBitmaps = new List<SKBitmap>(items.Count);
        var wrappedLabels = new List<IReadOnlyList<string>>(items.Count);
        try
        {
            var totalHeight = layout.Height;
            foreach (var item in items)
            {
                var bitmap = SKBitmap.Decode(RenderQrPng(item.Payload));
                qrBitmaps.Add(bitmap);

                var lines = WrapText(item.Label, labelPaint, maxTextWidth);
                wrappedLabels.Add(lines);

                totalHeight += CellHeight(lines.Count);
            }

            var info = new SKImageInfo(layout.Width, totalHeight);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            DrawHeaderBand(canvas, layout, titlePaint, fieldPaint, dividerPaint);

            var cellTop = layout.Height;
            for (var i = 0; i < items.Count; i++)
            {
                var lines = wrappedLabels[i];

                var qrLeft = (layout.Width - QrSize) / 2f;
                var qrTop = cellTop + CellPadding;
                var dest = new SKRect(qrLeft, qrTop, qrLeft + QrSize, qrTop + QrSize);
                canvas.DrawBitmap(qrBitmaps[i], dest);

                var textY = qrTop + QrSize + LabelGap + LabelTextSize;
                foreach (var line in lines)
                {
                    canvas.DrawText(line, layout.Width / 2f, textY, labelPaint);
                    textY += LabelLineHeight;
                }

                cellTop += CellHeight(lines.Count);
            }

            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        finally
        {
            foreach (var bitmap in qrBitmaps)
                bitmap.Dispose();
        }
    }

    /// <summary>Encodes a VietQR payload to bare PNG bytes (QRCoder <c>PngByteQRCode</c>, no chrome).</summary>
    private static byte[] RenderQrPng(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(SinglePixelsPerModule);
    }

    private static int CellHeight(int lineCount) =>
        CellPadding + QrSize + LabelGap + (int)(lineCount * LabelLineHeight) + CellPadding;

    private static SKPaint CreateHeaderPaint(SKTypeface typeface, float textSize) => new()
    {
        Typeface = typeface,
        TextSize = textSize,
        IsAntialias = true,
        Color = SKColors.Black,
        TextAlign = SKTextAlign.Left
    };

    /// <summary>The resolved image width, the wrapped title / bank-field lines, and the band height.</summary>
    private sealed record HeaderLayout(int Width, IReadOnlyList<string> TitleLines, IReadOnlyList<string> FieldLines, int Height);

    /// <summary>
    /// Composes each bank field as <c>"{label}: {value}"</c> and sizes the image so those fields fit
    /// INTACT — the width grows from <see cref="BaseImageWidth"/> to the widest field (+ padding),
    /// clamped at <see cref="MaxImageWidth"/>. Only if that cap is hit is a field ellipsized. The title
    /// still wraps to <see cref="MaxTitleLines"/> lines within the resolved width. Returns the width so
    /// both renderers size the canvas, centre the QR, and place the divider consistently.
    /// </summary>
    private static HeaderLayout BuildHeaderLayout(QrHeader header, SKPaint titlePaint, SKPaint fieldPaint)
    {
        // The bank fields must render in full; compose them first, then grow the image to fit them.
        var fields = new List<string>(4)
        {
            $"{header.BankLabel}: {header.BankName}",
            $"{header.HolderLabel}: {header.AccountHolderName}",
            $"{header.NumberLabel}: {header.AccountNumber}"
        };
        if (header.AmountLabel is not null && header.AmountText is not null)
            fields.Add($"{header.AmountLabel}: {header.AmountText}");

        var widestField = fields.Max(field => fieldPaint.MeasureText(field));
        var contentWidth = (int)Math.Ceiling(widestField) + (2 * HeaderPadding);
        var width = Math.Clamp(contentWidth, BaseImageWidth, MaxImageWidth);
        var maxTextWidth = width - (2 * HeaderPadding);

        // Intact by construction unless we hit MaxImageWidth (then Ellipsize trims to fit); a no-op otherwise.
        var fieldLines = fields.Select(field => Ellipsize(field, fieldPaint, maxTextWidth)).ToList();
        var titleLines = WrapText(header.Title, titlePaint, maxTextWidth, MaxTitleLines);

        var height = HeaderPadding
            + (int)(titleLines.Count * TitleLineHeight)
            + (int)HeaderTitleGap
            + (int)(fieldLines.Count * HeaderFieldLineHeight)
            + (int)HeaderBottomGap;

        return new HeaderLayout(width, titleLines, fieldLines, height);
    }

    /// <summary>Draws the left-aligned header band (title then bank fields) with an inset divider at its bottom.</summary>
    private static void DrawHeaderBand(SKCanvas canvas, HeaderLayout layout, SKPaint titlePaint, SKPaint fieldPaint, SKPaint dividerPaint)
    {
        const float x = HeaderPadding;

        var titleBaseline = HeaderPadding + TitleTextSize;
        foreach (var line in layout.TitleLines)
        {
            canvas.DrawText(line, x, titleBaseline, titlePaint);
            titleBaseline += TitleLineHeight;
        }

        var fieldBaseline = HeaderPadding + (layout.TitleLines.Count * TitleLineHeight) + HeaderTitleGap + HeaderFieldTextSize;
        foreach (var line in layout.FieldLines)
        {
            canvas.DrawText(line, x, fieldBaseline, fieldPaint);
            fieldBaseline += HeaderFieldLineHeight;
        }

        var dividerY = layout.Height - (DividerThickness / 2f);
        canvas.DrawLine(HeaderPadding, dividerY, layout.Width - HeaderPadding, dividerY, dividerPaint);
    }

    /// <summary>Word-wraps <paramref name="text"/> to fit <paramref name="maxWidth"/>, capped at <see cref="MaxLabelLines"/> lines (last line ellipsised if it overflows).</summary>
    private static IReadOnlyList<string> WrapText(string text, SKPaint paint, float maxWidth) =>
        WrapText(text, paint, maxWidth, MaxLabelLines);

    /// <summary>Word-wraps <paramref name="text"/> to fit <paramref name="maxWidth"/>, capped at <paramref name="maxLines"/> lines (last line ellipsised if it overflows).</summary>
    private static IReadOnlyList<string> WrapText(string text, SKPaint paint, float maxWidth, int maxLines)
    {
        var normalized = string.Join(' ', (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
            return new[] { string.Empty };

        var words = normalized.Split(' ');
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (paint.MeasureText(candidate) <= maxWidth || current.Length == 0)
            {
                current = candidate;
            }
            else
            {
                lines.Add(current);
                current = word;
                if (lines.Count == maxLines - 1)
                    break;
            }
        }

        // Whatever remains after the committed lines — the still-accumulating last line on natural
        // completion, or the full unconsumed tail when we broke at the line cap — goes on the final
        // line, ellipsised to fit. (On natural completion this remainder equals `current`; on an
        // early break it is the whole tail, so the overflow is shown as "…" rather than dropped.)
        var consumed = string.Join(' ', lines);
        var remainder = consumed.Length == 0 ? normalized : normalized[consumed.Length..].TrimStart();

        lines.Add(Ellipsize(remainder, paint, maxWidth));
        return lines;
    }

    private static string Ellipsize(string text, SKPaint paint, float maxWidth)
    {
        if (paint.MeasureText(text) <= maxWidth)
            return text;

        const string ellipsis = "…";
        var trimmed = text;
        while (trimmed.Length > 1 && paint.MeasureText(trimmed + ellipsis) > maxWidth)
            trimmed = trimmed[..^1];

        return trimmed + ellipsis;
    }

    private static SKTypeface LoadLabelTypeface()
    {
        // Prefer the bundled SIL-OFL font (portable to headless Linux/Docker).
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(FontResourceSuffix, StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                var typeface = SKTypeface.FromStream(stream);
                if (typeface is not null)
                    return typeface;
            }
        }

        // Fallback: a system typeface able to render Vietnamese diacritics (dev-only; flagged as a
        // portability gap - a headless container without the bundled font would need one installed).
        return SKFontManager.Default.MatchCharacter('ế') ?? SKTypeface.Default;
    }
}
