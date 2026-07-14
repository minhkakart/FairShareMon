namespace FairShareMonApi.Models.Members;

/// <summary>Yêu cầu đổi tên một thành viên.</summary>
public class UpdateMemberRequest
{
    /// <summary>Tên hiển thị mới của thành viên (1-100 ký tự).</summary>
    public string Name { get; set; } = string.Empty;
}
