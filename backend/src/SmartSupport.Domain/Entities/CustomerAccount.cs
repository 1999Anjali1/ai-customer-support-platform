using SmartSupport.Domain.Common;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Domain.Entities;

/// <summary>A telecom subscriber account (one mobile line).</summary>
public class CustomerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string AccountNumber { get; set; }
    public required string MobileNumber { get; set; }
    public required string FullName { get; set; }

    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    public decimal DataUsedGb { get; set; }
    public bool IsRoamingEnabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Bill> Bills { get; set; } = [];

    public bool HasOverdueBills => Bills.Any(b => b.Status == BillStatus.Overdue);

    /// <summary>
    /// Business rules for a plan change. The AI agent calls this through a tool,
    /// so the rules are enforced in code, not left to the language model.
    /// </summary>
    public void ChangePlan(Plan newPlan)
    {
        ArgumentNullException.ThrowIfNull(newPlan);

        if (!newPlan.IsActive)
            throw new DomainException($"The plan '{newPlan.NameEn}' is no longer available.");

        if (newPlan.Id == PlanId)
            throw new DomainException($"The customer is already on the '{newPlan.NameEn}' plan.");

        if (HasOverdueBills)
            throw new DomainException(
                "Plan changes are not allowed while the account has an overdue bill. " +
                "The overdue amount must be paid first.");

        PlanId = newPlan.Id;
        Plan = newPlan;
    }

    public void SetRoaming(bool enabled)
    {
        if (enabled && HasOverdueBills)
            throw new DomainException("Roaming cannot be activated while the account has an overdue bill.");

        IsRoamingEnabled = enabled;
    }
}
