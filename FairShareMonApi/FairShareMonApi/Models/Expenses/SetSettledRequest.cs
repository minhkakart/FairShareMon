namespace FairShareMonApi.Models.Expenses;

/// <summary>Yêu cầu đặt trạng thái đã trả (đã trả) cho phiếu chi tiêu.</summary>
public class SetSettledRequest
{
    /// <summary>True để đánh dấu đã trả, false để bỏ đánh dấu.</summary>
    public bool IsSettled { get; set; }
}
