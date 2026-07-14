namespace FairShareMonApi.Models.Export;

/// <summary>
/// Biểu diễn trung gian, độc lập định dạng của một tài liệu xuất (M8, OQ7). Do
/// <c>IExportService</c> dựng nên từ dữ liệu M5/M7 với mọi số tiền/ngày giờ đã được chuyển thành chuỗi
/// một lần duy nhất (OQ5/OQ6), rồi được <c>IExportFormatter</c> kết xuất ra byte theo từng định dạng.
/// Nhờ vậy thêm định dạng mới (Excel/JSON) chỉ cần thêm một formatter, không đổi phần dựng tài liệu.
/// </summary>
public sealed class ExportDocument
{
    /// <summary>Tiêu đề tài liệu (mô tả nội dung; các formatter có thể dùng hoặc bỏ qua).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Các phần (section) theo thứ tự: mỗi phần là khối nhãn/giá trị và/hoặc một bảng.</summary>
    public IReadOnlyList<ExportSection> Sections { get; init; } = [];
}

/// <summary>
/// Một phần của <see cref="ExportDocument"/>. Có thể là khối "nhãn: giá trị" (<see cref="HeaderFields"/>)
/// và/hoặc một bảng (<see cref="ColumnHeaders"/> + <see cref="Rows"/>). Mọi giá trị đã là chuỗi cuối
/// cùng để mọi formatter kết xuất nhất quán.
/// </summary>
public sealed class ExportSection
{
    /// <summary>Tên phần (tùy chọn), ví dụ "Cân bằng nợ"; kết xuất thành một dòng nhãn nếu có.</summary>
    public string? Name { get; init; }

    /// <summary>Khối nhãn/giá trị (tùy chọn), ví dụ "Tên phiếu" -&gt; "Ăn tối".</summary>
    public IReadOnlyList<KeyValuePair<string, string>>? HeaderFields { get; init; }

    /// <summary>Tiêu đề cột của bảng (tùy chọn); null nếu phần này không có bảng.</summary>
    public IReadOnlyList<string>? ColumnHeaders { get; init; }

    /// <summary>Các dòng dữ liệu của bảng (mỗi dòng là danh sách ô đã chuyển thành chuỗi).</summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = [];
}
