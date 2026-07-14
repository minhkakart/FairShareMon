namespace FairShareMonApi.Models.Members;

/// <summary>Thông tin thành viên trả về cho client.</summary>
public class MemberResponse
{
    public string Uuid { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>True nếu là thành viên đại diện chủ sổ (không thể xóa).</summary>
    public bool IsOwnerRepresentative { get; set; }

    /// <summary>True nếu thành viên đã bị xóa mềm (chỉ hiện khi yêu cầu kèm thành viên đã xóa).</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }
}
