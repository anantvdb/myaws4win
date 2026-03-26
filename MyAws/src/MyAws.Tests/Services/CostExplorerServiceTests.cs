using Amazon.CostExplorer;
using Amazon.CostExplorer.Model;
using FluentAssertions;
using NSubstitute;
using MyAws.Core.Services;

namespace MyAws.Tests.Services;

public class CostExplorerServiceTests : IDisposable
{
    private readonly IAmazonCostExplorer _mockCe = Substitute.For<IAmazonCostExplorer>();
    private readonly string _tempDir;

    public CostExplorerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "myaws-ce-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private CostExplorerService CreateSut() => new(_mockCe, _tempDir);

    private static GetCostAndUsageResponse BuildResponse(params (string service, string amount)[] items)
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        return new GetCostAndUsageResponse
        {
            ResultsByTime =
            [
                new ResultByTime
                {
                    TimePeriod = new DateInterval
                    {
                        Start = monthStart.ToString("yyyy-MM-dd"),
                        End = today.ToString("yyyy-MM-dd"),
                    },
                    Groups = items.Select(i => new Group
                    {
                        Keys = [i.service],
                        Metrics = new Dictionary<string, MetricValue>
                        {
                            ["BlendedCost"] = new() { Amount = i.amount, Unit = "USD" },
                        },
                    }).ToList(),
                }
            ],
        };
    }

    // ── GetMonthlyCostsAsync ──────────────────────────────────

    [Fact]
    public async Task GetMonthlyCosts_ReturnsTotalAndItems()
    {
        _mockCe.GetCostAndUsageAsync(Arg.Any<GetCostAndUsageRequest>(), Arg.Any<CancellationToken>())
            .Returns(BuildResponse(("Amazon EC2", "150.50"), ("Amazon S3", "12.25")));

        var sut = CreateSut();
        var (total, items) = await sut.GetMonthlyCostsAsync();

        total.Should().BeApproximately(162.75m, 0.01m);
        items.Should().HaveCount(2);
        items.Should().Contain(i => i.ServiceName == "Amazon EC2" && i.Amount == 150.50m);
        items.Should().Contain(i => i.ServiceName == "Amazon S3" && i.Amount == 12.25m);
    }

    [Fact]
    public async Task GetMonthlyCosts_ReturnsEmpty_WhenNoGroups()
    {
        _mockCe.GetCostAndUsageAsync(Arg.Any<GetCostAndUsageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetCostAndUsageResponse { ResultsByTime = [new ResultByTime { TimePeriod = new DateInterval { Start = "2026-03-01", End = "2026-03-26" }, Groups = [] }] });

        var sut = CreateSut();
        var (total, items) = await sut.GetMonthlyCostsAsync();

        total.Should().Be(0m);
        items.Should().BeEmpty();
    }

    // ── GetDailyCostsAsync ────────────────────────────────────

    [Fact]
    public async Task GetDailyCosts_AggregatesPerDay()
    {
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        _mockCe.GetCostAndUsageAsync(Arg.Any<GetCostAndUsageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetCostAndUsageResponse
            {
                ResultsByTime =
                [
                    new ResultByTime
                    {
                        TimePeriod = new DateInterval { Start = yesterday.ToString("yyyy-MM-dd"), End = today.ToString("yyyy-MM-dd") },
                        Groups =
                        [
                            new Group { Keys = ["EC2"], Metrics = new() { ["BlendedCost"] = new() { Amount = "50.00", Unit = "USD" } } },
                            new Group { Keys = ["S3"], Metrics = new() { ["BlendedCost"] = new() { Amount = "5.00", Unit = "USD" } } },
                        ],
                    },
                    new ResultByTime
                    {
                        TimePeriod = new DateInterval { Start = today.ToString("yyyy-MM-dd"), End = today.AddDays(1).ToString("yyyy-MM-dd") },
                        Groups =
                        [
                            new Group { Keys = ["EC2"], Metrics = new() { ["BlendedCost"] = new() { Amount = "45.00", Unit = "USD" } } },
                        ],
                    },
                ],
            });

        var sut = CreateSut();
        var items = await sut.GetDailyCostsAsync();

        items.Should().HaveCount(2);
        items[0].Amount.Should().BeApproximately(55.00m, 0.01m);
        items[1].Amount.Should().BeApproximately(45.00m, 0.01m);
    }

    // ── Caching ───────────────────────────────────────────────

    [Fact]
    public async Task GetMonthlyCosts_UsesCachedResult_OnSecondCall()
    {
        _mockCe.GetCostAndUsageAsync(Arg.Any<GetCostAndUsageRequest>(), Arg.Any<CancellationToken>())
            .Returns(BuildResponse(("Amazon EC2", "100.00")));

        var sut = CreateSut();
        await sut.GetMonthlyCostsAsync();
        await sut.GetMonthlyCostsAsync();

        // API should only be called once; second call reads from disk cache
        await _mockCe.Received(1).GetCostAndUsageAsync(
            Arg.Any<GetCostAndUsageRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMonthlyCosts_ReturnsEmpty_OnApiException()
    {
        _mockCe.GetCostAndUsageAsync(Arg.Any<GetCostAndUsageRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetCostAndUsageResponse>(_ => throw new Exception("DataUnavailable"));

        var sut = CreateSut();
        var act = () => sut.GetMonthlyCostsAsync();

        await act.Should().NotThrowAsync();
        var (total, items) = await sut.GetMonthlyCostsAsync();
        total.Should().Be(0m);
        items.Should().BeEmpty();
    }
}
