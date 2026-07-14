using FairShareMonApi.Models.Export;

namespace FairShareMonApi.Services.Api.Export;

/// <summary>
/// Kết xuất một <see cref="ExportDocument"/> độc lập định dạng ra byte theo một định dạng cụ thể (M8,
/// OQ7). Mỗi định dạng là một lớp cài đặt riêng, đăng ký
/// <c>[ScopedService(typeof(IExportFormatter), Multiple = true)]</c> để cùng tồn tại (CLAUDE.md cảnh báo
/// <c>TryAdd</c> không Multiple sẽ bỏ các đăng ký sau). Thêm Excel/JSON về sau = thêm một lớp formatter,
/// không đổi controller/service.
/// </summary>
public interface IExportFormatter
{
    /// <summary>Định dạng mà formatter này xử lý.</summary>
    ExportFormat Format { get; }

    /// <summary>Content-Type dùng cho HTTP response, ví dụ <c>text/csv; charset=utf-8</c>.</summary>
    string ContentType { get; }

    /// <summary>Phần mở rộng tệp (không có dấu chấm), ví dụ <c>csv</c>.</summary>
    string FileExtension { get; }

    /// <summary>Kết xuất tài liệu thành byte theo định dạng của formatter.</summary>
    byte[] Render(ExportDocument document);
}
