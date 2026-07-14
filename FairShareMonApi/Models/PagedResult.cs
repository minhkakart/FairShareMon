namespace FairShareMonApi.Models;

/// <summary>
/// Generic paged result envelope: the current page's items plus paging metadata. Introduced by M11
/// for the admin user listing; reusable by any future paged endpoint.
/// </summary>
public class PagedResult<T>
{
    /// <summary>Items on the current page.</summary>
    public IReadOnlyList<T> Items { get; set; } = [];

    /// <summary>1-based page number.</summary>
    public int Page { get; set; }

    /// <summary>Page size used.</summary>
    public int PageSize { get; set; }

    /// <summary>Total number of matching items across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>Total number of pages for the current page size (0 when empty).</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
