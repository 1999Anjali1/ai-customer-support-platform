using SmartSupport.Domain.Enums;

namespace SmartSupport.Domain.Entities;

public class Bill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerAccountId { get; set; }

    /// <summary>First day of the billing month.</summary>
    public DateOnly Period { get; set; }
    public decimal AmountQar { get; set; }
    public DateOnly DueDate { get; set; }
    public BillStatus Status { get; set; } = BillStatus.Unpaid;
    public DateTime? PaidAt { get; set; }
}
