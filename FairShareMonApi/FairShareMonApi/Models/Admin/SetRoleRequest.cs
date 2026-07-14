namespace FairShareMonApi.Models.Admin;

/// <summary>Yêu cầu thăng/giáng vai trò của một người dùng (M11 OQ9): <c>Role</c> = USER hoặc ADMIN.</summary>
public class SetRoleRequest
{
    /// <summary>Vai trò mới: USER hoặc ADMIN.</summary>
    public string Role { get; set; } = string.Empty;
}
