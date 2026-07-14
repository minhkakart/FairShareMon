namespace FairShareMonApi.Models.Events;

/// <summary>Bộ lọc danh sách đợt chi tiêu (OQ10).</summary>
public class EventFilter
{
    /// <summary>Lọc theo trạng thái đã chốt, tùy chọn (true = đã chốt, false = đang mở).</summary>
    public bool? Closed { get; set; }
}
