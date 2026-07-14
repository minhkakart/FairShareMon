namespace FairShareMonApi.Models.Expenses;

/// <summary>Một dòng nhật ký thay đổi trả về cho client (§3.8).</summary>
public class AuditLogResponse
{
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Loại đối tượng: <c>Expense</c> hoặc <c>Share</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>UUID của đối tượng đã thay đổi (phiếu hoặc phần gánh).</summary>
    public string EntityUuid { get; set; } = string.Empty;

    /// <summary>Hành động: <c>Create</c>, <c>Update</c> hoặc <c>Delete</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Trạng thái trước khi thay đổi (null khi tạo mới).</summary>
    public object? Before { get; set; }

    /// <summary>Trạng thái sau khi thay đổi (null khi xóa).</summary>
    public object? After { get; set; }

    public DateTime CreatedAt { get; set; }
}
