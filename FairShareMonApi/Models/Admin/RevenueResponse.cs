namespace FairShareMonApi.Models.Admin;

/// <summary>Doanh thu trong một mốc thời gian: nhãn + tổng tiền + số lượt cấp.</summary>
public class RevenueBucketRow
{
    public string PeriodLabel { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public int GrantCount { get; set; }
}

/// <summary>
/// Bảng doanh thu (M11 OQ14): tổng theo từng mốc + tổng chung + danh sách mã tham chiếu. Chỉ tính các
/// dòng GRANT (REVOKE không tính doanh thu). Nguồn dữ liệu duy nhất là <c>tier_grants</c> (R10).
/// </summary>
public class RevenueResponse
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    /// <summary>Độ chia thời gian đã dùng (month|day).</summary>
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Doanh thu theo từng mốc thời gian.</summary>
    public IReadOnlyList<RevenueBucketRow> Buckets { get; set; } = [];

    /// <summary>Tổng doanh thu trong khoảng.</summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>Tổng số lượt cấp (GRANT) trong khoảng.</summary>
    public int GrantCount { get; set; }

    /// <summary>Danh sách mã tham chiếu thanh toán (nếu có), mới nhất trước.</summary>
    public IReadOnlyList<string> References { get; set; } = [];
}
