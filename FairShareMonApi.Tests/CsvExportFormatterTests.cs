using System.Collections.Generic;
using System.Text;
using FairShareMonApi.Models.Export;
using FairShareMonApi.Services.Api.Export.Formatters;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="CsvExportFormatter"/> (M8, OQ2/OQ3/OQ4/OQ7). Proves the CSV
/// bytes begin with the UTF-8 BOM (<c>EF BB BF</c>) so Excel renders Vietnamese; the content-type /
/// extension / format identity; that a header-field section renders as <c>label,value</c> rows, a table
/// renders its column headers then data rows, section names appear, sections are blank-line separated,
/// every line ends CRLF, RFC-4180 quoting is applied in-context, and Vietnamese text round-trips
/// (decode → original).
/// </summary>
public class CsvExportFormatterTests
{
    private static readonly CsvExportFormatter Formatter = new();

    private static string DecodeBody(byte[] bytes)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        Assert.True(bytes.Length >= preamble.Length);
        return Encoding.UTF8.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
    }

    [Fact]
    public void Identity_ContentTypeExtensionAndFormat()
    {
        Assert.Equal("text/csv; charset=utf-8", Formatter.ContentType);
        Assert.Equal("csv", Formatter.FileExtension);
        Assert.Equal(ExportFormat.Csv, Formatter.Format);
    }

    [Fact]
    public void Render_StartsWithUtf8Bom()
    {
        var bytes = Formatter.Render(new ExportDocument { Title = "x", Sections = [] });

        Assert.True(bytes.Length >= 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void Render_HeaderFieldsSection_EmitsLabelValueRows()
    {
        var document = new ExportDocument
        {
            Sections =
            [
                new ExportSection
                {
                    HeaderFields = new List<KeyValuePair<string, string>>
                    {
                        new("Tên phiếu", "Ăn tối"),
                        new("Tổng tiền", "800000.00")
                    }
                }
            ]
        };

        var text = DecodeBody(Formatter.Render(document));

        Assert.Contains("Tên phiếu,Ăn tối\r\n", text);
        Assert.Contains("Tổng tiền,800000.00\r\n", text);
    }

    [Fact]
    public void Render_TableSection_EmitsNameHeadersThenRows()
    {
        var document = new ExportDocument
        {
            Sections =
            [
                new ExportSection
                {
                    Name = "Cân bằng nợ",
                    ColumnHeaders = ["Thành viên", "Cân bằng"],
                    Rows =
                    [
                        ["Bình", "300000.00"],
                        ["Cường", "-500000.00"]
                    ]
                }
            ]
        };

        var text = DecodeBody(Formatter.Render(document));

        Assert.Contains("Cân bằng nợ\r\n", text);
        Assert.Contains("Thành viên,Cân bằng\r\n", text);
        Assert.Contains("Bình,300000.00\r\n", text);
        Assert.Contains("Cường,-500000.00\r\n", text);
    }

    [Fact]
    public void Render_MultipleSections_AreBlankLineSeparated()
    {
        var document = new ExportDocument
        {
            Sections =
            [
                new ExportSection { HeaderFields = [new KeyValuePair<string, string>("A", "1")] },
                new ExportSection { HeaderFields = [new KeyValuePair<string, string>("B", "2")] }
            ]
        };

        var text = DecodeBody(Formatter.Render(document));

        // A blank line (CRLF CRLF) separates the two sections.
        Assert.Contains("A,1\r\n\r\nB,2\r\n", text);
    }

    [Fact]
    public void Render_EveryLineEndsWithCrLf_IncludingLast()
    {
        var document = new ExportDocument
        {
            Sections = [new ExportSection { HeaderFields = [new KeyValuePair<string, string>("A", "1")] }]
        };

        var text = DecodeBody(Formatter.Render(document));

        Assert.EndsWith("\r\n", text);
    }

    [Fact]
    public void Render_FieldWithComma_IsQuotedInContext()
    {
        var document = new ExportDocument
        {
            Sections =
            [
                new ExportSection
                {
                    ColumnHeaders = ["Ghi chú"],
                    Rows = [["Ăn tối: chia đều; Taxi, về sân bay"]]
                }
            ]
        };

        var text = DecodeBody(Formatter.Render(document));

        Assert.Contains("\"Ăn tối: chia đều; Taxi, về sân bay\"\r\n", text);
    }

    [Fact]
    public void Render_VietnameseText_RoundTripsThroughUtf8()
    {
        var document = new ExportDocument
        {
            Sections = [new ExportSection { HeaderFields = [new KeyValuePair<string, string>("Đợt", "Đà Lạt - Bình")] }]
        };

        var text = DecodeBody(Formatter.Render(document));

        Assert.Contains("Đợt,Đà Lạt - Bình", text);
    }
}
