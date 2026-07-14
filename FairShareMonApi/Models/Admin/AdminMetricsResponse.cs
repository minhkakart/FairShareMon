namespace FairShareMonApi.Models.Admin;

/// <summary>Một cặp khóa-số đếm trong một phân bố (ví dụ hạng -&gt; số người dùng).</summary>
public class MetricCount
{
    public string Key { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>Số đăng ký trong một mốc thời gian.</summary>
public class PeriodMetric
{
    public string PeriodLabel { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>
/// Bảng chỉ số quản trị (M11 OQ6): các con số CHỈ dựa trên metadata tài khoản (người dùng/hạng/vai trò/
/// trạng thái + đăng ký theo thời gian). TUYỆT ĐỐI không có số liệu sổ chi tiêu, kể cả ẩn danh (R10).
/// </summary>
public class AdminMetricsResponse
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    /// <summary>Tổng số người dùng.</summary>
    public int TotalUsers { get; set; }

    /// <summary>Phân bố theo hạng (FREE/PREMIUM).</summary>
    public IReadOnlyList<MetricCount> TierDistribution { get; set; } = [];

    /// <summary>Phân bố theo vai trò (USER/ADMIN).</summary>
    public IReadOnlyList<MetricCount> RoleDistribution { get; set; } = [];

    /// <summary>Phân bố theo trạng thái (ACTIVE/DISABLED).</summary>
    public IReadOnlyList<MetricCount> StatusDistribution { get; set; } = [];

    /// <summary>Số lượng đăng ký theo mốc thời gian (theo <c>Bucket</c>).</summary>
    public IReadOnlyList<PeriodMetric> Signups { get; set; } = [];
}
