namespace FairShareMonApi.Models.Tags;

/// <summary>Yêu cầu thêm nhãn mới.</summary>
public class CreateTagRequest
{
    /// <summary>Tên nhãn (1-100 ký tự).</summary>
    public string Name { get; set; } = string.Empty;
}
