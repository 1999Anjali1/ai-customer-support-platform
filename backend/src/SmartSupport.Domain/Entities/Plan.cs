namespace SmartSupport.Domain.Entities;

/// <summary>A mobile plan in the catalogue (e.g. "Smart 100 – QAR 100/month").</summary>
public class Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string NameEn { get; set; }
    public required string NameAr { get; set; }
    public decimal MonthlyPriceQar { get; set; }

    /// <summary>Monthly data in GB. <c>null</c> means unlimited.</summary>
    public int? DataGb { get; set; }

    /// <summary>Local minutes. <c>null</c> means unlimited.</summary>
    public int? LocalMinutes { get; set; }

    public int InternationalMinutes { get; set; }
    public bool Is5G { get; set; }
    public bool IsActive { get; set; } = true;
}
