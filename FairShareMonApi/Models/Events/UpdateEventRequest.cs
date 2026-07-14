namespace FairShareMonApi.Models.Events;

/// <summary>Yêu cầu cập nhật thông tin đợt chi tiêu (chỉ khi đợt đang mở). Khoảng thời gian là trọn ngày (bao gồm hai đầu, theo UTC).</summary>
public class UpdateEventRequest
{
    /// <summary>Tên đợt chi tiêu (1-200 ký tự).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Mô tả tùy chọn (tối đa 1000 ký tự).</summary>
    public string? Description { get; set; }

    /// <summary>Ngày bắt đầu (được chuẩn hóa về 00:00:00 UTC của ngày đó).</summary>
    public DateTime StartDate { get; set; }

    /// <summary>Ngày kết thúc (được chuẩn hóa về 23:59:59.999999 UTC của ngày đó). Phải sau hoặc bằng ngày bắt đầu.</summary>
    public DateTime EndDate { get; set; }
}
