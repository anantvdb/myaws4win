using MyAws.Core.Models;

namespace MyAws.Core.Services;

public interface ICostExplorerService
{
    Task<(decimal MonthlyTotal, List<MonthlyCostItem> Items)> GetMonthlyCostsAsync(CancellationToken ct = default);
    Task<List<DailyCostItem>> GetDailyCostsAsync(CancellationToken ct = default);
}
