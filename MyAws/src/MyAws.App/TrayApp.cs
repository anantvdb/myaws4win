using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using MyAws.Core.Configuration;
using MyAws.Core.Models;
using MyAws.Core.Services;

namespace MyAws.App;

public sealed class TrayApp : IDisposable
{
    private readonly AppConfig _config;
    private readonly ConfigManager _configManager;
    private readonly IEc2Service _ec2;
    private readonly ICostExplorerService _costs;
    private readonly IPricingService _pricing;
    private readonly SshService _ssh;

    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    private AppSnapshot? _snapshot;
    private DateTime? _lastRefresh;
    private string? _lastError;
    private bool _isRefreshing;

    public TrayApp(
        AppConfig config,
        ConfigManager configManager,
        IEc2Service ec2,
        ICostExplorerService costs,
        IPricingService pricing,
        SshService ssh)
    {
        _config = config;
        _configManager = configManager;
        _ec2 = ec2;
        _costs = costs;
        _pricing = pricing;
        _ssh = ssh;

        _trayIcon = new NotifyIcon
        {
            Text = "MyAWS",
            Icon = CreateTrayIcon(),
        };

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = _config.RefreshIntervalSeconds * 1000,
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public void Start()
    {
        RebuildMenu();
        _trayIcon.Visible = true;
        _refreshTimer.Start();

        // Initial refresh
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    // ── Refresh ──────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        _lastError = null;
        RebuildMenu();

        try
        {
            var imagesTask = _ec2.GetOwnedImagesAsync(_config.AwsOwnerId);
            var instancesTask = _ec2.GetAllInstancesAsync();
            var volumesTask = _ec2.GetVolumesSummaryAsync();
            var snapshotsTask = _ec2.GetSnapshotsSummaryAsync(_config.AwsOwnerId);
            var monthlyCostsTask = _costs.GetMonthlyCostsAsync();
            var dailyCostsTask = _costs.GetDailyCostsAsync();

            await Task.WhenAll(imagesTask, instancesTask, volumesTask, snapshotsTask, monthlyCostsTask, dailyCostsTask);

            var images = await imagesTask;
            var instances = await instancesTask;
            var (volCount, volGb) = await volumesTask;
            var (snapCount, snapGb) = await snapshotsTask;
            var (monthlyTotal, monthlyItems) = await monthlyCostsTask;
            var dailyItems = await dailyCostsTask;

            // Group instances by image
            var byImage = new Dictionary<string, List<InstanceInfo>>();
            foreach (var inst in instances)
            {
                if (!byImage.TryGetValue(inst.ImageId, out var list))
                {
                    list = [];
                    byImage[inst.ImageId] = list;
                }
                list.Add(inst);
            }

            _snapshot = new AppSnapshot
            {
                Timestamp = DateTime.Now,
                Images = images,
                InstancesByImage = byImage,
                VolumesCount = volCount,
                VolumesGb = volGb,
                SnapshotsCount = snapCount,
                SnapshotsGb = snapGb,
                MonthlyTotal = monthlyTotal,
                MonthlyCostItems = monthlyItems,
                DailyCostItems = dailyItems,
            };

            _lastRefresh = DateTime.Now;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
        }
        finally
        {
            _isRefreshing = false;
            RebuildMenu();
        }
    }

    // ── Menu Building ────────────────────────────────────────

    private void RebuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Title
        var title = BuildTitleText();
        menu.Items.Add(new ToolStripMenuItem(title) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        // Refresh / Update pricing
        menu.Items.Add(AsyncMenuItem("Refresh now", RefreshAsync));
        var pricingLabel = _pricing.LastUpdated.HasValue
            ? $"Update AWS pricing (last: {_pricing.LastUpdated.Value:yyyy-MM-dd HH:mm})"
            : "Update AWS pricing (never fetched)";
        menu.Items.Add(AsyncMenuItem(pricingLabel, UpdatePricingAsync));
        menu.Items.Add(new ToolStripSeparator());

        // Images
        if (_snapshot != null && _snapshot.Images.Count > 0)
        {
            foreach (var image in _snapshot.Images.OrderBy(i => i.Name))
                menu.Items.Add(BuildImageMenu(image));
        }
        else if (_snapshot != null)
        {
            var noAmis = new ToolStripMenuItem("No AMIs found");
            noAmis.DropDownItems.Add(BuildVmPricingMenu("Available VM options"));
            menu.Items.Add(noAmis);
        }
        else if (_isRefreshing)
        {
            menu.Items.Add(new ToolStripMenuItem("Loading...") { Enabled = false });
        }
        else if (_lastError != null)
        {
            menu.Items.Add(new ToolStripMenuItem($"Error: {Truncate(_lastError, 60)}") { Enabled = false });
        }
        else
        {
            menu.Items.Add(new ToolStripMenuItem("No data") { Enabled = false });
        }

        menu.Items.Add(new ToolStripSeparator());

        // Storage
        menu.Items.Add(BuildStorageMenu());

        // Costs
        menu.Items.Add(BuildCostsMenu());

        menu.Items.Add(new ToolStripSeparator());

        // Utilities
        menu.Items.Add("Open config", null, (_, _) =>
        {
            var path = _configManager.ConfigPath;
            if (File.Exists(path))
                Process.Start("explorer.exe", $"/select,\"{path}\"");
        });

        menu.Items.Add("Open state folder", null, (_, _) =>
        {
            var dir = ConfigManager.StateDirectory(_config);
            Process.Start("explorer.exe", dir);
        });

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Quit", null, (_, _) =>
        {
            Dispose();
            Application.Exit();
        });

        _trayIcon.ContextMenuStrip = menu;
    }

    private string BuildTitleText()
    {
        if (_isRefreshing)
            return "MyAWS | refreshing...";

        if (_lastError != null)
            return $"MyAWS | error";

        if (_snapshot == null)
            return "MyAWS";

        var imageCount = _snapshot.Images.Count;
        var refreshTime = _lastRefresh?.ToString("HH:mm") ?? "?";
        return $"MyAWS | {imageCount} image{(imageCount != 1 ? "s" : "")} | refreshed {refreshTime}";
    }

    // ── Image Menu ───────────────────────────────────────────

    private ToolStripMenuItem BuildImageMenu(ImageInfo image)
    {
        var item = new ToolStripMenuItem(image.Name ?? image.ImageId);

        // Deploy new VM submenu
        item.DropDownItems.Add(BuildDeployMenu(image));

        // Instances submenu
        item.DropDownItems.Add(BuildInstancesMenu(image));

        item.DropDownItems.Add(new ToolStripSeparator());

        // Image actions
        var hasSnapshot = !string.IsNullOrEmpty(image.SnapshotId);

        item.DropDownItems.Add(AsyncMenuItem("Image: Update", () => UpdateImageAsync(image), hasSnapshot));
        item.DropDownItems.Add(AsyncMenuItem("Image: Rebuild", () => RebuildImageAsync(image), hasSnapshot));
        item.DropDownItems.Add(AsyncMenuItem("Image: Destroy", () => DestroyImageAsync(image), hasSnapshot));

        return item;
    }

    private ToolStripMenuItem BuildDeployMenu(ImageInfo image)
    {
        var deploy = new ToolStripMenuItem("Deploy new VM");

        foreach (var group in _config.VmTypes)
        {
            var familyMenu = new ToolStripMenuItem(group.Prefix);

            foreach (var vmType in group.Types)
            {
                var fullName = vmType.FullName(group.Prefix);
                var price = _pricing.FormatPrice(fullName);
                var label = $"{fullName} ({vmType.Description}) {price}";
                var capturedFullName = fullName;
                familyMenu.DropDownItems.Add(AsyncMenuItem(label,
                    () => DeployVmAsync(image.ImageId, capturedFullName)));
            }

            deploy.DropDownItems.Add(familyMenu);
        }

        return deploy;
    }

    private ToolStripMenuItem BuildInstancesMenu(ImageInfo image)
    {
        var instancesMenu = new ToolStripMenuItem("Instances");

        List<InstanceInfo>? instances = null;
        _snapshot?.InstancesByImage.TryGetValue(image.ImageId, out instances);

        if (instances == null || instances.Count == 0)
        {
            instancesMenu.DropDownItems.Add(new ToolStripMenuItem("No instances") { Enabled = false });
            return instancesMenu;
        }

        foreach (var inst in instances)
        {
            var uptime = FormatUptime(inst.LaunchTime);
            var ip = !string.IsNullOrEmpty(inst.PublicIp) ? inst.PublicIp : "no ip";
            var label = $"{inst.InstanceId} | {inst.State} | {inst.InstanceType} | {ip} | {uptime}";
            var instMenu = new ToolStripMenuItem(label);

            var state = inst.State.ToLowerInvariant();
            var isRunning = state == "running";
            var isStopped = state == "stopped";
            var canTerminate = state is "running" or "stopped" or "pending" or "stopping";
            var isNotTerminated = state != "terminated" && state != "shutting-down";
            var hasDns = !string.IsNullOrEmpty(inst.PublicDns);

            instMenu.DropDownItems.Add(AsyncMenuItem("Connect SSH",
                () => { _ssh.OpenTerminal(inst.PublicDns); return Task.CompletedTask; },
                isRunning && hasDns));

            instMenu.DropDownItems.Add(AsyncMenuItem("Start",
                () => StartInstanceAsync(inst.InstanceId), isStopped));

            instMenu.DropDownItems.Add(AsyncMenuItem("Stop",
                () => StopInstanceAsync(inst.InstanceId), isRunning));

            instMenu.DropDownItems.Add(AsyncMenuItem("Terminate",
                () => TerminateInstanceAsync(inst.InstanceId), canTerminate));

            instMenu.DropDownItems.Add(AsyncMenuItem("Create image",
                () => CreateImageFromInstanceAsync(inst), isStopped));

            instMenu.DropDownItems.Add(AsyncMenuItem("Save serial console log",
                () => SaveConsoleLogAsync(inst.InstanceId), isNotTerminated));

            instMenu.DropDownItems.Add(AsyncMenuItem("Get screenshot",
                () => SaveScreenshotAsync(inst.InstanceId), isRunning));

            instancesMenu.DropDownItems.Add(instMenu);
        }

        return instancesMenu;
    }

    // ── Storage / Costs Menus ────────────────────────────────

    private ToolStripMenuItem BuildStorageMenu()
    {
        var storage = new ToolStripMenuItem("Storage");
        if (_snapshot != null)
        {
            storage.DropDownItems.Add(new ToolStripMenuItem(
                $"Volumes: {_snapshot.VolumesCount} objects, {_snapshot.VolumesGb} GiB") { Enabled = false });
            storage.DropDownItems.Add(new ToolStripMenuItem(
                $"Snapshots: {_snapshot.SnapshotsCount} objects, {_snapshot.SnapshotsGb} GiB") { Enabled = false });
        }
        else
        {
            storage.DropDownItems.Add(new ToolStripMenuItem("No data") { Enabled = false });
        }
        return storage;
    }

    private ToolStripMenuItem BuildCostsMenu()
    {
        var costsMenu = new ToolStripMenuItem("Costs");
        if (_snapshot == null)
        {
            costsMenu.DropDownItems.Add(new ToolStripMenuItem("No data") { Enabled = false });
            return costsMenu;
        }

        costsMenu.DropDownItems.Add(new ToolStripMenuItem(
            $"Month total: {_snapshot.MonthlyTotal:F2} USD") { Enabled = false });

        var monthlyBreakdown = new ToolStripMenuItem("Monthly breakdown");
        foreach (var item in _snapshot.MonthlyCostItems)
            monthlyBreakdown.DropDownItems.Add(new ToolStripMenuItem(
                $"{item.ServiceName}: {item.Amount:F2} {item.Unit}") { Enabled = false });
        if (_snapshot.MonthlyCostItems.Count == 0)
            monthlyBreakdown.DropDownItems.Add(new ToolStripMenuItem("No data") { Enabled = false });
        costsMenu.DropDownItems.Add(monthlyBreakdown);

        var dailyTotals = new ToolStripMenuItem("Daily totals");
        foreach (var item in _snapshot.DailyCostItems)
            dailyTotals.DropDownItems.Add(new ToolStripMenuItem(
                $"{item.Date}: {item.Amount:F2} USD") { Enabled = false });
        if (_snapshot.DailyCostItems.Count == 0)
            dailyTotals.DropDownItems.Add(new ToolStripMenuItem("No data") { Enabled = false });
        costsMenu.DropDownItems.Add(dailyTotals);

        return costsMenu;
    }

    private ToolStripMenuItem BuildVmPricingMenu(string label)
    {
        var menu = new ToolStripMenuItem(label);
        foreach (var group in _config.VmTypes)
            foreach (var vmType in group.Types)
            {
                var fullName = vmType.FullName(group.Prefix);
                var price = _pricing.FormatPrice(fullName);
                menu.DropDownItems.Add(new ToolStripMenuItem(
                    $"{fullName} ({vmType.Description}) — {price}") { Enabled = false });
            }
        return menu;
    }

    // ── Actions ──────────────────────────────────────────────

    private async Task UpdatePricingAsync()
    {
        await _pricing.UpdateAllPricesAsync(_config.VmTypes);
        ShowBalloon("Pricing updated", "AWS pricing cache has been refreshed.");
        RebuildMenu();
    }

    private async Task DeployVmAsync(string imageId, string instanceType)
    {
        var id = await _ec2.RunInstanceAsync(imageId, instanceType, _config.AwsKeyName, _config.AwsSecurityGroupId);
        ShowBalloon("VM deployed", $"Instance {id} launching as {instanceType}.");
        await RefreshAsync();
    }

    private async Task StartInstanceAsync(string instanceId)
    {
        await _ec2.StartInstanceAsync(instanceId);
        ShowBalloon("Instance starting", instanceId);
        await RefreshAsync();
    }

    private async Task StopInstanceAsync(string instanceId)
    {
        await _ec2.StopInstanceAsync(instanceId);
        ShowBalloon("Instance stopping", instanceId);
        await RefreshAsync();
    }

    private async Task TerminateInstanceAsync(string instanceId)
    {
        var result = MessageBox.Show(
            $"Terminate instance {instanceId}?\n\nThis action cannot be undone.",
            "Confirm Terminate", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        await _ec2.TerminateInstanceAsync(instanceId);
        ShowBalloon("Instance terminating", instanceId);
        await RefreshAsync();
    }

    private async Task CreateImageFromInstanceAsync(InstanceInfo instance)
    {
        var name = $"{instance.InstanceType}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var imageId = await _ec2.CreateImageAsync(instance.InstanceId, name);
        ShowBalloon("Creating image", $"Image {imageId} from {instance.InstanceId}.");
        await _ec2.WaitForImageAvailableAsync(imageId);
        ShowBalloon("Image ready", $"{imageId} is now available.");
        await RefreshAsync();
    }

    private async Task SaveConsoleLogAsync(string instanceId)
    {
        var output = await _ec2.GetConsoleOutputAsync(instanceId);
        var path = Path.Combine(Path.GetTempPath(), $"console-{instanceId}.log");
        await File.WriteAllTextAsync(path, output);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task SaveScreenshotAsync(string instanceId)
    {
        var bytes = await _ec2.GetConsoleScreenshotAsync(instanceId);
        var path = Path.Combine(Path.GetTempPath(), $"screenshot-{instanceId}.png");
        await File.WriteAllBytesAsync(path, bytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task UpdateImageAsync(ImageInfo image)
    {
        var vmType = _config.DefaultVmTypeUpdate;
        var result = MessageBox.Show(
            $"Update image '{image.Name}'?\n\nThis will deploy a {vmType} instance, run '{_config.UpdateCommand}', create a new image, and clean up.",
            "Confirm Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        ShowBalloon("Updating image", $"Launching {vmType} for '{image.Name}'...");

        // Deploy
        var instanceId = await _ec2.RunInstanceAsync(image.ImageId, vmType, _config.AwsKeyName, _config.AwsSecurityGroupId);
        await _ec2.WaitForInstanceRunningAsync(instanceId);

        // Get DNS
        var inst = await _ec2.DescribeInstanceAsync(instanceId);
        if (string.IsNullOrEmpty(inst.PublicDns))
        {
            ShowBalloon("Update failed", "Instance has no public DNS.");
            await _ec2.TerminateInstanceAsync(instanceId);
            return;
        }

        // Wait a bit for SSH to be ready, then run command
        await Task.Delay(30_000);
        var exitCode = _ssh.RunRemoteCommand(inst.PublicDns, _config.UpdateCommand);
        if (exitCode != 0)
        {
            ShowBalloon("Update failed", $"Remote command exited with code {exitCode}. Instance {instanceId} left running.");
            return;
        }

        // Stop, create image, clean up
        await _ec2.StopInstanceAsync(instanceId);
        await _ec2.WaitForInstanceStoppedAsync(instanceId);

        var newName = $"{image.Name}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var newImageId = await _ec2.CreateImageAsync(instanceId, newName);
        await _ec2.WaitForImageAvailableAsync(newImageId);

        await _ec2.TerminateInstanceAsync(instanceId);
        await _ec2.DeregisterImageAsync(image.ImageId);
        if (!string.IsNullOrEmpty(image.SnapshotId))
            await _ec2.DeleteSnapshotAsync(image.SnapshotId);

        ShowBalloon("Image updated", $"New image: {newImageId}");
        await RefreshAsync();
    }

    private async Task RebuildImageAsync(ImageInfo image)
    {
        var vmType = _config.DefaultVmTypeRebuild;
        var result = MessageBox.Show(
            $"Rebuild image '{image.Name}'?\n\nThis will deploy a {vmType} instance, run '{_config.RebuildCommand}', create a new image, and clean up.\n\nThis can take a long time.",
            "Confirm Rebuild", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        ShowBalloon("Rebuilding image", $"Launching {vmType} for '{image.Name}'...");

        var instanceId = await _ec2.RunInstanceAsync(image.ImageId, vmType, _config.AwsKeyName, _config.AwsSecurityGroupId);
        await _ec2.WaitForInstanceRunningAsync(instanceId);

        var inst = await _ec2.DescribeInstanceAsync(instanceId);
        if (string.IsNullOrEmpty(inst.PublicDns))
        {
            ShowBalloon("Rebuild failed", "Instance has no public DNS.");
            await _ec2.TerminateInstanceAsync(instanceId);
            return;
        }

        await Task.Delay(30_000);
        var exitCode = _ssh.RunRemoteCommand(inst.PublicDns, _config.RebuildCommand);
        if (exitCode != 0)
        {
            ShowBalloon("Rebuild failed", $"Remote command exited with code {exitCode}. Instance {instanceId} left running.");
            return;
        }

        await _ec2.StopInstanceAsync(instanceId);
        await _ec2.WaitForInstanceStoppedAsync(instanceId);

        var newName = $"{image.Name}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var newImageId = await _ec2.CreateImageAsync(instanceId, newName);
        await _ec2.WaitForImageAvailableAsync(newImageId);

        await _ec2.TerminateInstanceAsync(instanceId);
        await _ec2.DeregisterImageAsync(image.ImageId);
        if (!string.IsNullOrEmpty(image.SnapshotId))
            await _ec2.DeleteSnapshotAsync(image.SnapshotId);

        ShowBalloon("Image rebuilt", $"New image: {newImageId}");
        await RefreshAsync();
    }

    private async Task DestroyImageAsync(ImageInfo image)
    {
        var result = MessageBox.Show(
            $"Destroy image '{image.Name}'?\n\nThis will deregister the AMI and delete its snapshot.\nThis action cannot be undone.",
            "Confirm Destroy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        // Terminate all instances of this image first
        if (_snapshot?.InstancesByImage.TryGetValue(image.ImageId, out var instances) == true && instances.Count > 0)
        {
            var confirm2 = MessageBox.Show(
                $"This image has {instances.Count} instance(s). Terminate them all?",
                "Confirm Terminate All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm2 != DialogResult.Yes) return;

            await _ec2.TerminateInstancesAsync(instances.Select(i => i.InstanceId));
        }

        await _ec2.DeregisterImageAsync(image.ImageId);
        if (!string.IsNullOrEmpty(image.SnapshotId))
            await _ec2.DeleteSnapshotAsync(image.SnapshotId);

        ShowBalloon("Image destroyed", image.Name ?? image.ImageId);
        await RefreshAsync();
    }

    // ── Helpers ──────────────────────────────────────────────

    private ToolStripMenuItem AsyncMenuItem(string text, Func<Task> action, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text);
        item.Enabled = enabled;
        item.Click += (_, _) => _ = RunWithErrorHandling(text, action);
        return item;
    }

    private async Task RunWithErrorHandling(string actionName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{actionName} failed:\n\n{ex.Message}", "MyAWS Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowBalloon(string title, string text)
    {
        _trayIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    private static string FormatUptime(DateTime? launchTime)
    {
        if (launchTime == null) return "";
        var span = DateTime.UtcNow - launchTime.Value.ToUniversalTime();
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}d:{span.Hours:D2}h:{span.Minutes:D2}m";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h:{span.Minutes:D2}m";
        return $"{(int)span.TotalMinutes}m";
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 3)] + "...";

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bgBrush = new SolidBrush(Color.FromArgb(33, 150, 243));
            g.FillRectangle(bgBrush, 0, 0, 32, 32);
            using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            var size = g.MeasureString("AWS", font);
            g.DrawString("AWS", font, textBrush, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
