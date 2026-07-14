using System.Reflection;
using DiDecoration.Attributes;
using QRCoder;
using SkiaSharp;

namespace FairShareMonApi.Services.Api.Wallet;

/// <summary>
/// Renders VietQR payloads to images (OQ2/OQ3). <see cref="RenderSingle"/> produces the single-expense
/// QR as PNG bytes via QRCoder's <c>PngByteQRCode</c> (no <c>System.Drawing</c>, cross-platform).
/// <see cref="RenderComposite"/> produces the event QR as one raster PNG via SkiaSharp: each owing
/// member's QR plus a name + amount label, vertically stacked into a single shareable image
/// (§3.10 "gửi vào nhóm chat"). Labels use a bundled SIL-OFL Vietnamese font (Be Vietnam Pro) so
/// diacritics render on headless Linux/Docker; if the embedded font is unavailable it falls back to a
/// system typeface that can render Vietnamese.
/// </summary>
public interface IQrImageService
{
    /// <summary>Renders a single VietQR payload to PNG bytes (QRCoder <c>PngByteQRCode</c>).</summary>
    byte[] RenderSingle(string payload);

    /// <summary>Renders a labelled composite PNG stacking one QR per item (member name + amount label under each), via SkiaSharp.</summary>
    byte[] RenderComposite(IReadOnlyList<QrCompositeItem> items);
}

/// <summary>One entry of the event-QR composite: the human-readable label and its VietQR payload.</summary>
public sealed record QrCompositeItem(string Label, string Payload);

[ScopedService(typeof(IQrImageService))]
public sealed class QrImageService : IQrImageService
{
    private const int SinglePixelsPerModule = 20;

    // Composite layout constants (device pixels).
    private const int ImageWidth = 380;
    private const int CellPadding = 24;
    private const int QrSize = 280;
    private const int LabelGap = 12;
    private const float LabelTextSize = 24f;
    private const float LabelLineHeight = 32f;
    private const int MaxLabelLines = 3;

    /// <summary>The bundled font resource name suffix (matches regardless of the assembly's default namespace prefix).</summary>
    private const string FontResourceSuffix = "BeVietnamPro-Regular.ttf";

    // Loaded once: embedded Be Vietnam Pro, else a Vietnamese-capable system fallback.
    private static readonly Lazy<SKTypeface> LabelTypeface = new(LoadLabelTypeface);

    public byte[] RenderSingle(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        return qrCode.GetGraphic(SinglePixelsPerModule);
    }

    public byte[] RenderComposite(IReadOnlyList<QrCompositeItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
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

        var maxTextWidth = ImageWidth - (2 * CellPadding);

        // Pre-render each QR bitmap and wrap each label so total height is known before drawing.
        var qrBitmaps = new List<SKBitmap>(items.Count);
        var wrappedLabels = new List<IReadOnlyList<string>>(items.Count);
        try
        {
            var totalHeight = 0;
            foreach (var item in items)
            {
                var bitmap = SKBitmap.Decode(RenderSingle(item.Payload));
                qrBitmaps.Add(bitmap);

                var lines = WrapText(item.Label, labelPaint, maxTextWidth);
                wrappedLabels.Add(lines);

                totalHeight += CellHeight(lines.Count);
            }

            var info = new SKImageInfo(ImageWidth, totalHeight);
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            var cellTop = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var lines = wrappedLabels[i];

                var qrLeft = (ImageWidth - QrSize) / 2f;
                var qrTop = cellTop + CellPadding;
                var dest = new SKRect(qrLeft, qrTop, qrLeft + QrSize, qrTop + QrSize);
                canvas.DrawBitmap(qrBitmaps[i], dest);

                var textY = qrTop + QrSize + LabelGap + LabelTextSize;
                foreach (var line in lines)
                {
                    canvas.DrawText(line, ImageWidth / 2f, textY, labelPaint);
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

    private static int CellHeight(int lineCount) =>
        CellPadding + QrSize + LabelGap + (int)(lineCount * LabelLineHeight) + CellPadding;

    /// <summary>Word-wraps a label to fit <paramref name="maxWidth"/>, capped at <see cref="MaxLabelLines"/> lines (last line ellipsised if it overflows).</summary>
    private static IReadOnlyList<string> WrapText(string text, SKPaint paint, float maxWidth)
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
                if (lines.Count == MaxLabelLines - 1)
                    break;
            }
        }

        // Whatever remains (including an overflow tail) goes on the final line, ellipsised to fit.
        var consumed = string.Join(' ', lines);
        var remainder = consumed.Length == 0 ? normalized : normalized[consumed.Length..].TrimStart();
        if (current.Length > 0 && lines.Count < MaxLabelLines)
            remainder = current;

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
