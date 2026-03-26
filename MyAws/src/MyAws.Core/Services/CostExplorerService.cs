using System.Text.Json;
using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using MyAws.Core.Models;

namespace MyAws.Core.Services;

public sealed class CostExplorerService : ICostExplorerService
{
    private readonly IAmazonCostExplorer _costExplorer;
    private readonly string _stateDir;

    public CostExplorerService(IAmazonCostExplorer costExplorer, string stateDir)
    {
        _costExplorer = costExplorer;
        _stateDir = stateDir;
    }

    public async Task<(decimal MonthlyTotal, List<MonthlyCostItem> Items)> GetMonthlyCostsAsync(CancellationToken ct = default)
    {
        var payload = await GetCostPayloadAsync("MONTHLY", ct);
        var items = new List<MonthlyCostItem>();
        var total = 0m;

        foreach (var period in payload)
        foreach (var group in period.Groups)
        {
            var amount = decimal.Parse(group.Metrics["BlendedCost"].Amount,
                System.Globalization.CultureInfo.InvariantCulture);
            total += amount;
            items.Add(new MonthlyCostItem
            {
                ServiceName = group.Keys[0],
                Amount = amount,
                Unit = group.Metrics["BlendedCost"].Unit,
            });
        }

        return (total, items);
    }

    public async Task<List<DailyCostItem>> GetDailyCostsAsync(CancellationToken ct = default)
    {
        var payload = await GetCostPayloadAsync("DAILY", ct);
        var items = new List<DailyCostItem>();

        foreach (var period in payload)
        {
            var dayTotal = 0m;
            foreach (var group in period.Groups)
                dayTotal += decimal.Parse(group.Metrics["BlendedCost"].Amount,
                    System.Globalization.CultureInfo.InvariantCulture);

            items.Add(new DailyCostItem
            {
                Date = period.TimePeriod.Start,
                Amount = dayTotal,
            });
        }

        return items;
    }

    private async Task<List<ResultByTime>> GetCostPayloadAsync(string granularity, CancellationToken ct)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        if (today == monthStart)
            monthStart = monthStart.AddDays(-1);
        monthStart = new DateTime(monthStart.Year, monthStart.Month, 1);

        var cacheFile = Path.Combine(_stateDir, $"myaws-costs-{granularity.ToLower()}-{today:yyyyMMdd}.json");
        if (File.Exists(cacheFile))
        {
            var cached = await File.ReadAllTextAsync(cacheFile, ct);
            return JsonSerializer.Deserialize<List<ResultByTimeCache>>(cached)
                ?.Select(c => c.ToResultByTime()).ToList()
                ?? [];
        }

        try
        {
            var response = await _costExplorer.GetCostAndUsageAsync(new GetCostAndUsageRequest
            {
                TimePeriod = new DateInterval
                {
                    Start = monthStart.ToString("yyyy-MM-dd"),
                    End = today.ToString("yyyy-MM-dd"),
                },
                Granularity = new Granularity(granularity),
                Metrics = ["BlendedCost"],
                GroupBy = [new GroupDefinition { Type = GroupDefinitionType.DIMENSION, Key = "SERVICE" }],
            }, ct);

            // Cache the results
            var cacheData = response.ResultsByTime.Select(ResultByTimeCache.From).ToList();
            Directory.CreateDirectory(_stateDir);
            await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(cacheData), ct);

            return response.ResultsByTime;
        }
        catch (Exception ex) when (ex.Message.Contains("DataUnavailable") || ex.Message.Contains("GetCostAndUsage"))
        {
            return [];
        }
    }

    // Simple cache DTOs to avoid serializing AWS SDK types directly
    private sealed class ResultByTimeCache
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public List<GroupCache> Groups { get; set; } = [];

        public static ResultByTimeCache From(ResultByTime r) => new()
        {
            Start = r.TimePeriod.Start,
            End = r.TimePeriod.End,
            Groups = r.Groups.Select(g => new GroupCache
            {
                Keys = g.Keys,
                Amount = g.Metrics["BlendedCost"].Amount,
                Unit = g.Metrics["BlendedCost"].Unit,
            }).ToList(),
        };

        public ResultByTime ToResultByTime() => new()
        {
            TimePeriod = new DateInterval { Start = Start, End = End },
            Groups = Groups.Select(g => new Group
            {
                Keys = g.Keys,
                Metrics = new Dictionary<string, MetricValue>
                {
                    ["BlendedCost"] = new() { Amount = g.Amount, Unit = g.Unit },
                },
            }).ToList(),
        };
    }

    private sealed class GroupCache
    {
        public List<string> Keys { get; set; } = [];
        public string Amount { get; set; } = "0";
        public string Unit { get; set; } = "USD";
    }
}
