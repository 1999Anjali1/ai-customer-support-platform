using Microsoft.EntityFrameworkCore;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Accounts;

public sealed class AccountService(IAppDbContext db)
{
    /// <summary>The logged-in customer's account card (plan, data usage, bills).</summary>
    public async Task<AccountDto?> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var accountId = await db.Users.Where(u => u.Id == userId).Select(u => u.CustomerAccountId).FirstOrDefaultAsync(ct);
        if (accountId is null) return null;

        var account = await db.CustomerAccounts.AsNoTracking()
            .Include(a => a.Plan)
            .Include(a => a.Bills)
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);

        if (account is null) return null;

        return new AccountDto(
            account.AccountNumber,
            account.MobileNumber,
            account.FullName,
            account.Plan.ToDto(),
            account.DataUsedGb,
            account.IsRoamingEnabled,
            account.Bills.Where(b => b.Status != BillStatus.Paid).Sum(b => b.AmountQar),
            account.Bills.OrderByDescending(b => b.Period).Take(6).Select(b => b.ToDto()).ToList());
    }

    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var plans = await db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.MonthlyPriceQar).ToListAsync(ct);
        return plans.Select(p => p.ToDto()).ToList();
    }
}
