using System.Text.Json;
using FluentAssertions;
using MyAws.Core.Configuration;
using MyAws.Core.Models;

namespace MyAws.Tests.Configuration;

public class ConfigManagerTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "myaws-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string TempConfigPath() => Path.Combine(_tempDir, "config.json");

    [Fact]
    public void Load_CreatesDefaultConfig_WhenFileDoesNotExist()
    {
        var path = TempConfigPath();
        var mgr = new ConfigManager(path);

        var config = mgr.Load();

        File.Exists(path).Should().BeTrue();
        config.AwsRegion.Should().Be("eu-central-1");
        config.RefreshIntervalSeconds.Should().Be(900);
        config.VmTypes.Should().NotBeEmpty();
        config.AwsOwnerId.Should().BeEmpty("defaults should not contain personal data");
    }

    [Fact]
    public void Load_RoundTrips_SavedConfig()
    {
        var path = TempConfigPath();
        var mgr = new ConfigManager(path);

        var original = new AppConfig
        {
            AwsOwnerId = "123456789012",
            AwsRegion = "us-west-2",
            PreferredCurrency = "USD",
            RefreshIntervalSeconds = 300,
        };
        mgr.Save(original);

        var loaded = mgr.Load();

        loaded.AwsOwnerId.Should().Be("123456789012");
        loaded.AwsRegion.Should().Be("us-west-2");
        loaded.PreferredCurrency.Should().Be("USD");
        loaded.RefreshIntervalSeconds.Should().Be(300);
    }

    [Fact]
    public void Load_MergesPartialConfig_WithDefaults()
    {
        var path = TempConfigPath();
        // Write a partial config (only some fields)
        var partial = new { awsOwnerId = "111222333444", awsRegion = "ap-southeast-1" };
        File.WriteAllText(path, JsonSerializer.Serialize(partial));

        var mgr = new ConfigManager(path);
        var config = mgr.Load();

        // User-specified values
        config.AwsOwnerId.Should().Be("111222333444");
        config.AwsRegion.Should().Be("ap-southeast-1");

        // Default values preserved
        config.SshUser.Should().Be("root");
        config.RefreshIntervalSeconds.Should().Be(900);
        config.VmTypes.Should().NotBeEmpty();
    }

    [Fact]
    public void Save_CreatesParentDirectories()
    {
        var path = Path.Combine(_tempDir, "nested", "deep", "config.json");
        var mgr = new ConfigManager(path);

        mgr.Save(new AppConfig());

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        var path = TempConfigPath();
        var mgr = new ConfigManager(path);

        mgr.Save(new AppConfig { AwsOwnerId = "test" });

        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("awsOwnerId").GetString().Should().Be("test");
    }

    [Fact]
    public void DefaultVmTypes_ContainsExpectedFamilies()
    {
        var types = ConfigDefaults.DefaultVmTypes();

        types.Should().HaveCountGreaterThanOrEqualTo(7);
        types.Select(t => t.Prefix).Should().Contain("m7i");
        types.Select(t => t.Prefix).Should().Contain("c7i");
        types.Select(t => t.Prefix).Should().Contain("u-6tb1");

        // Verify descriptions say RAM not vram
        foreach (var group in types)
        foreach (var type in group.Types)
            type.Description.Should().Contain("RAM").And.NotContain("vram");
    }
}
