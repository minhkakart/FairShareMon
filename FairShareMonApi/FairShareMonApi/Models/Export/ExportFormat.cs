namespace FairShareMonApi.Models.Export;

/// <summary>
/// Định dạng xuất được hỗ trợ (M8, OQ7). Hiện chỉ có <see cref="Csv"/>; thêm Excel/JSON về sau chỉ là
/// thêm một giá trị enum và một lớp <c>IExportFormatter</c> mới, không đụng controller/service.
/// </summary>
public enum ExportFormat
{
    /// <summary>Xuất CSV (RFC-4180, UTF-8 có BOM) - định dạng cơ bản (OQ2/OQ3/OQ4).</summary>
    Csv
}
