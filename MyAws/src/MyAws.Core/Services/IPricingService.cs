using MyAws.Core.Models;

namespace MyAws.Core.Services;

public interface IPricingService
{
    DateTime? LastUpdated { get; }
    Task<decimal?> GetOnDemandPriceAsync(string instanceType, CancellationToken ct = default);
    Task UpdateAllPricesAsync(List<VmTypeGroup> vmTypes, CancellationToken ct = default);
    string FormatPrice(string instanceType);
}
