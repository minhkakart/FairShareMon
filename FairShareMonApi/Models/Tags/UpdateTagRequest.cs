namespace FairShareMonApi.Models.Tags;

/// <summary>Yêu cầu đổi tên một nhãn.</summary>
public class UpdateTagRequest
{
    /// <summary>Tên nhãn mới (1-100 ký tự).</summary>
    public string Name { get; set; } = string.Empty;
}
