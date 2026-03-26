using System.Text.Json;
using Amazon.Pricing;
using Amazon.Pricing.Model;
using FluentAssertions;
using NSubstitute;
using MyAws.Core.Models;
using MyAws.Core.Services;

namespace MyAws.Tests.Services;

public class PricingServiceTests : IDisposable
{
    private readonly IAmazonPricing _mockPricing = Substitute.For<IAmazonPricing>();
    private readonly string _tempDir;

    public PricingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "myaws-pricing-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PricingService CreateSut() =>
        new(_mockPricing, _tempDir, "EU (Frankfurt)", "Linux");

    // ── FormatPrice ───────────────────────────────────────────

    [Fact]
    public void FormatPrice_ReturnsNa_WhenCacheEmpty()
    {
        var sut = CreateSut();

        sut.FormatPrice("m5.4xlarge").Should().Be("n/a");
    }

    [Fact]
    public async Task FormatPrice_ReturnsUsdPrice_AfterUpdate()
    {
        SetupPricingResponse("m5.4xlarge", "0.7680");
        var sut = CreateSut();
        await sut.UpdateAllPricesAsync([new VmTypeGroup { Prefix = "m5", Types = [new VmTypeOption { Suffix = ".4xlarge" }] }]);

        var result = sut.FormatPrice("m5.4xlarge");

        result.Should().Contain("USD/h");
        result.Should().Contain("0.7680");
        result.Should().NotContain("€");
        result.Should().NotContain("$");
    }

    // ── LastUpdated ───────────────────────────────────────────

    [Fact]
    public void LastUpdated_IsNull_WhenNoCacheFile()
    {
        var sut = CreateSut();

        sut.LastUpdated.Should().BeNull();
    }

    [Fact]
    public async Task LastUpdated_IsSet_AfterUpdateAllPrices()
    {
        SetupPricingResponse("m5.4xlarge", "0.7680");
        var sut = CreateSut();
        var before = DateTime.Now;

        await sut.UpdateAllPricesAsync([new VmTypeGroup { Prefix = "m5", Types = [new VmTypeOption { Suffix = ".4xlarge" }] }]);

        sut.LastUpdated.Should().NotBeNull();
        sut.LastUpdated!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task LastUpdated_IsRestoredFromDisk_WhenServiceRestarted()
    {
        SetupPricingResponse("m5.4xlarge", "0.7680");
        var sut = CreateSut();
        await sut.UpdateAllPricesAsync([new VmTypeGroup { Prefix = "m5", Types = [new VmTypeOption { Suffix = ".4xlarge" }] }]);
        var savedTime = sut.LastUpdated;

        // Simulate restart by creating a new instance that reads the same cache dir
        var sut2 = CreateSut();

        sut2.LastUpdated.Should().BeCloseTo(savedTime!.Value, TimeSpan.FromSeconds(1));
    }

    // ── UpdateAllPricesAsync ──────────────────────────────────

    [Fact]
    public async Task UpdateAllPrices_SkipsTypes_WhenApiThrows()
    {
        _mockPricing.GetProductsAsync(Arg.Any<GetProductsRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetProductsResponse>(_ => throw new Exception("API error"));

        var sut = CreateSut();
        var act = () => sut.UpdateAllPricesAsync([new VmTypeGroup { Prefix = "m5", Types = [new VmTypeOption { Suffix = ".4xlarge" }] }]);

        await act.Should().NotThrowAsync();
        sut.FormatPrice("m5.4xlarge").Should().Be("n/a");
    }

    [Fact]
    public void UpdateAllPrices_HandlesBadCacheFile_Gracefully()
    {
        // Write a corrupt cache file before constructing the service
        File.WriteAllText(Path.Combine(_tempDir, "pricing-cache.json"), "not valid json {{");

        var sut = CreateSut(); // should not throw on load

        sut.LastUpdated.Should().BeNull();
        sut.FormatPrice("m5.4xlarge").Should().Be("n/a");
    }

    // ── Helpers ───────────────────────────────────────────────

    private void SetupPricingResponse(string instanceType, string usdPrice)
    {
        var priceDoc = JsonSerializer.Serialize(new
        {
            terms = new
            {
                OnDemand = new Dictionary<string, object>
                {
                    ["term1"] = new
                    {
                        priceDimensions = new Dictionary<string, object>
                        {
                            ["dim1"] = new
                            {
                                pricePerUnit = new { USD = usdPrice }
                            }
                        }
                    }
                }
            }
        });

        _mockPricing.GetProductsAsync(
                Arg.Is<GetProductsRequest>(r => r.Filters.Any(f => f.Value == instanceType)),
                Arg.Any<CancellationToken>())
            .Returns(new GetProductsResponse { PriceList = [priceDoc] });
    }
}
