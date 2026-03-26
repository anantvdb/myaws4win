using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.Pricing;
using Amazon.Pricing.Model;
using MyAws.Core.Models;

namespace MyAws.Core.Services;

public sealed class PricingService : IPricingService
{
    private readonly IAmazonPricing _pricing;
    private readonly string _stateDir;
    private readonly string _location;
    private readonly string _operatingSystem;
    private PriceCache _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PricingService(IAmazonPricing pricing, string stateDir, string location, string operatingSystem)
    {
        _pricing = pricing;
        _stateDir = stateDir;
        _location = location;
        _operatingSystem = operatingSystem;
        _cache = LoadCache();
    }

    public DateTime? LastUpdated => _cache.UpdatedAt;

    private string CacheFilePath => Path.Combine(_stateDir, "pricing-cache.json");

    private PriceCache LoadCache()
    {
        if (!File.Exists(CacheFilePath))
            return new PriceCache();

        try
        {
            var json = File.ReadAllText(CacheFilePath);
            return JsonSerializer.Deserialize<PriceCache>(json, JsonOptions) ?? new PriceCache();
        }
        catch
        {
            return new PriceCache();
        }
    }

    private void SaveCache()
    {
        Directory.CreateDirectory(_stateDir);
        File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(_cache, JsonOptions));
    }

    public async Task<decimal?> GetOnDemandPriceAsync(string instanceType, CancellationToken ct = default)
    {
        var response = await _pricing.GetProductsAsync(new GetProductsRequest
        {
            ServiceCode = "AmazonEC2",
            MaxResults = 1,
            Filters =
            [
                new Filter { Type = FilterType.TERM_MATCH, Field = "instanceType", Value = instanceType },
                new Filter { Type = FilterType.TERM_MATCH, Field = "operatingSystem", Value = _operatingSystem },
                new Filter { Type = FilterType.TERM_MATCH, Field = "tenancy", Value = "Shared" },
                new Filter { Type = FilterType.TERM_MATCH, Field = "location", Value = _location },
                new Filter { Type = FilterType.TERM_MATCH, Field = "licenseModel", Value = "No License required" },
                new Filter { Type = FilterType.TERM_MATCH, Field = "preInstalledSw", Value = "NA" },
                new Filter { Type = FilterType.TERM_MATCH, Field = "capacitystatus", Value = "Used" },
            ],
        }, ct);

        if (response.PriceList.Count == 0) return null;

        var doc = JsonDocument.Parse(response.PriceList[0]);
        var onDemand = doc.RootElement.GetProperty("terms").GetProperty("OnDemand");

        foreach (var term in onDemand.EnumerateObject())
        foreach (var dimension in term.Value.GetProperty("priceDimensions").EnumerateObject())
        {
            var usd = dimension.Value.GetProperty("pricePerUnit").GetProperty("USD").GetString();
            if (!string.IsNullOrEmpty(usd) && decimal.TryParse(usd,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var price))
                return price;
        }

        return null;
    }

    public async Task UpdateAllPricesAsync(List<VmTypeGroup> vmTypes, CancellationToken ct = default)
    {
        var prices = new Dictionary<string, decimal>();

        foreach (var group in vmTypes)
        foreach (var type in group.Types)
        {
            var fullName = type.FullName(group.Prefix);
            try
            {
                var price = await GetOnDemandPriceAsync(fullName, ct);
                if (price.HasValue)
                    prices[fullName] = price.Value;
            }
            catch
            {
                // Skip types that fail to price
            }
        }

        _cache = new PriceCache
        {
            UpdatedAt = DateTime.Now,
            Prices = prices,
        };
        SaveCache();
    }

    public string FormatPrice(string instanceType)
    {
        if (!_cache.Prices.TryGetValue(instanceType, out var usdPrice))
            return "n/a";

        return $"{usdPrice.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)} USD/h";
    }

    private sealed class PriceCache
    {
        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }

        [JsonPropertyName("prices")]
        public Dictionary<string, decimal> Prices { get; set; } = [];
    }
}
