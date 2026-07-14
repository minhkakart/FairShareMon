using System.Text;
using DiDecoration.Attributes;
using FairShareMonApi.Models.Export;

namespace FairShareMonApi.Services.Api.Export.Formatters;

/// <summary>
/// Kết xuất <see cref="ExportDocument"/> ra CSV (M8, OQ2/OQ3/OQ4). Với mỗi phần: ghi tên phần (nếu có),
/// khối "nhãn,giá trị", rồi (nếu có bảng) một dòng trống ngăn cách, dòng tiêu đề cột và các dòng dữ
/// liệu; các phần cách nhau bằng một dòng trống. Escaping theo RFC-4180 (<see cref="CsvWriter"/>), kết
/// thúc dòng CRLF, mã hóa UTF-8 CÓ BOM để Excel hiển thị đúng tiếng Việt (OQ3). Đăng ký
/// <c>Multiple = true</c> để các formatter tương lai (Excel/JSON) cùng tồn tại.
/// </summary>
[ScopedService(typeof(IExportFormatter), Multiple = true)]
public sealed class CsvExportFormatter : IExportFormatter
{
    public ExportFormat Format => ExportFormat.Csv;

    public string ContentType => "text/csv; charset=utf-8";

    public string FileExtension => "csv";

    public byte[] Render(ExportDocument document)
    {
        var lines = new List<string>();

        for (var i = 0; i < document.Sections.Count; i++)
        {
            if (i > 0)
                lines.Add(string.Empty); // dòng trống ngăn cách các phần

            var section = document.Sections[i];
            var wroteInSection = false;

            if (section.Name is not null)
            {
                lines.Add(CsvWriter.FormatRow([section.Name]));
                wroteInSection = true;
            }

            if (section.HeaderFields is not null)
            {
                foreach (var field in section.HeaderFields)
                    lines.Add(CsvWriter.FormatRow([field.Key, field.Value]));
                wroteInSection = true;
            }

            if (section.ColumnHeaders is not null)
            {
                if (wroteInSection)
                    lines.Add(string.Empty); // ngăn cách khối header với bảng trong cùng một phần

                lines.Add(CsvWriter.FormatRow(section.ColumnHeaders));
                foreach (var row in section.Rows)
                    lines.Add(CsvWriter.FormatRow(row));
            }
        }

        // Kết thúc mọi dòng bằng CRLF (kể cả dòng cuối).
        var text = string.Join(CsvWriter.LineEnding, lines) + CsvWriter.LineEnding;

        var preamble = Encoding.UTF8.GetPreamble(); // BOM EF BB BF (OQ3)
        var body = Encoding.UTF8.GetBytes(text);
        return [.. preamble, .. body];
    }
}
