using System.Text.Json;
using Amazon;
using Amazon.CostExplorer;
using Amazon.EC2;
using Amazon.Pricing;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Microsoft.Extensions.DependencyInjection;
using MyAws.Core.Configuration;
using MyAws.Core.Models;
using MyAws.Core.Services;

namespace MyAws.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handle CLI modes before starting GUI
        if (args.Contains("--update-pricing") || args.Contains("--snapshot"))
        {
            RunCliMode(args).GetAwaiter().GetResult();
            return;
        }

        // Single-instance check
        using var mutex = new Mutex(true, "MyAWS_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("MyAWS is already running.", "MyAWS",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Build DI container
        var configPath = GetArgValue(args, "--config");
        var configManager = new ConfigManager(configPath);
        var config = configManager.Load();
        var stateDir = ConfigManager.StateDirectory(config);

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(configManager);

        var credentials = ResolveCredentials(config);
        var ec2Region = RegionEndpoint.GetBySystemName(config.AwsRegion);

        services.AddSingleton<IAmazonEC2>(_ => credentials != null
            ? new AmazonEC2Client(credentials, ec2Region)
            : new AmazonEC2Client(ec2Region));

        services.AddSingleton<IAmazonCostExplorer>(_ => credentials != null
            ? new AmazonCostExplorerClient(credentials, ec2Region)
            : new AmazonCostExplorerClient(ec2Region));

        services.AddSingleton<IAmazonPricing>(_ => credentials != null
            ? new AmazonPricingClient(credentials, RegionEndpoint.USEast1)
            : new AmazonPricingClient(RegionEndpoint.USEast1));

        services.AddSingleton<IEc2Service, Ec2Service>();
        services.AddSingleton<ICostExplorerService>(sp =>
            new CostExplorerService(sp.GetRequiredService<IAmazonCostExplorer>(), stateDir));
        services.AddSingleton<IPricingService>(sp =>
            new PricingService(sp.GetRequiredService<IAmazonPricing>(), stateDir, config.AwsLocationName, config.AwsOperatingSystem));
        services.AddSingleton(_ =>
            new SshService(config.SshUser, ResolveSshKnownHosts(config)));
        services.AddSingleton<TrayApp>();

        using var provider = services.BuildServiceProvider();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var trayApp = provider.GetRequiredService<TrayApp>();
        trayApp.Start();
        Application.Run();
        trayApp.Dispose();
    }

    private static AWSCredentials? ResolveCredentials(AppConfig config)
    {
        if (string.IsNullOrEmpty(config.AwsProfile))
            return null;

        var chain = new CredentialProfileStoreChain();
        if (chain.TryGetAWSCredentials(config.AwsProfile, out var creds))
            return creds;

        return null;
    }

    private static string ResolveSshKnownHosts(AppConfig config)
    {
        if (!string.IsNullOrEmpty(config.SshKnownHostsFile))
            return config.SshKnownHostsFile;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh", "amazon-vms");
    }

    private static async Task RunCliMode(string[] args)
    {
        var configPath = GetArgValue(args, "--config");
        var configManager = new ConfigManager(configPath);
        var config = configManager.Load();
        var stateDir = ConfigManager.StateDirectory(config);

        AWSCredentials? credentials = null;
        if (!string.IsNullOrEmpty(config.AwsProfile))
        {
            var chain = new CredentialProfileStoreChain();
            chain.TryGetAWSCredentials(config.AwsProfile, out credentials);
        }

        var region = RegionEndpoint.GetBySystemName(config.AwsRegion);

        if (args.Contains("--update-pricing"))
        {
            using var pricingClient = credentials != null
                ? new AmazonPricingClient(credentials, RegionEndpoint.USEast1)
                : new AmazonPricingClient(RegionEndpoint.USEast1);

            var service = new PricingService(pricingClient, stateDir, config.AwsLocationName, config.AwsOperatingSystem);
            await service.UpdateAllPricesAsync(config.VmTypes);
            Console.WriteLine("Pricing updated.");
        }

        if (args.Contains("--snapshot"))
        {
            using var ec2Client = credentials != null
                ? new AmazonEC2Client(credentials, region)
                : new AmazonEC2Client(region);
            using var ceClient = credentials != null
                ? new AmazonCostExplorerClient(credentials, region)
                : new AmazonCostExplorerClient(region);

            var ec2 = new Ec2Service(ec2Client);
            var costs = new CostExplorerService(ceClient, stateDir);

            var images = await ec2.GetOwnedImagesAsync(config.AwsOwnerId);
            var instances = await ec2.GetAllInstancesAsync();
            var (volCount, volGb) = await ec2.GetVolumesSummaryAsync();
            var (snapCount, snapGb) = await ec2.GetSnapshotsSummaryAsync(config.AwsOwnerId);
            var (monthlyTotal, monthlyItems) = await costs.GetMonthlyCostsAsync();
            var dailyItems = await costs.GetDailyCostsAsync();

            var snapshot = new
            {
                timestamp = DateTime.Now.ToString("o"),
                images,
                instances,
                volumesCount = volCount,
                volumesGb = volGb,
                snapshotsCount = snapCount,
                snapshotsGb = snapGb,
                monthlyTotal,
                monthlyCostItems = monthlyItems,
                dailyCostItems = dailyItems,
            };

            Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        }
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
