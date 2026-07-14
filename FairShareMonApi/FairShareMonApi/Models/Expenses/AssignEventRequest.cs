namespace FairShareMonApi.Models.Expenses;

/// <summary>Yêu cầu gán (hoặc chuyển) một phiếu chi tiêu vào một đợt (OQ4/OQ16).</summary>
public class AssignEventRequest
{
    /// <summary>UUID đợt chi tiêu đích. Đợt phải thuộc cùng tài khoản, đang mở và chứa thời điểm chi của phiếu.</summary>
    public string EventUuid { get; set; } = string.Empty;
}
