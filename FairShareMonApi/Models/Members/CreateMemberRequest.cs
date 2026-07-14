namespace FairShareMonApi.Models.Members;

/// <summary>Yêu cầu thêm thành viên mới vào sổ.</summary>
public class CreateMemberRequest
{
    /// <summary>Tên hiển thị của thành viên (1-100 ký tự).</summary>
    public string Name { get; set; } = string.Empty;
}
