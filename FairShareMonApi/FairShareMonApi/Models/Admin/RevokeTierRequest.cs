namespace FairShareMonApi.Models.Admin;

/// <summary>Yêu cầu thu hồi Premium (hạ xuống Free) của một người dùng (M11 OQ4). Chỉ có ghi chú tùy chọn.</summary>
public class RevokeTierRequest
{
    /// <summary>Ghi chú của admin (tùy chọn).</summary>
    public string? Note { get; set; }
}
