namespace FairShareMonApi.Models.Tags;

/// <summary>Thông tin nhãn trả về cho client.</summary>
public class TagResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>True nếu nhãn đã bị xóa mềm (chỉ hiện khi yêu cầu kèm nhãn đã xóa).</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }
}
