using System.Text.Json;
using System.Text.Json.Nodes;
using MyAws.Core.Models;

namespace MyAws.Core.Configuration;

public sealed class ConfigManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _configPath;

    public ConfigManager(string? configPath = null)
    {
        _configPath = configPath ?? DefaultConfigPath();
    }

    public string ConfigPath => _configPath;

    public static string DefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            return Path.Combine(appData, "MyAWS", "config.json");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".state", "myaws", "config.json");
    }

    public static string StateDirectory(AppConfig config)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = !string.IsNullOrEmpty(appData)
            ? Path.Combine(appData, "MyAWS")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".state", "myaws");

        Directory.CreateDirectory(dir);
        return dir;
    }

    public AppConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            var defaults = new AppConfig();
            Save(defaults);
            return defaults;
        }

        var json = File.ReadAllText(_configPath);
        var userNode = JsonNode.Parse(json);
        if (userNode is null)
            return new AppConfig();

        var defaultConfig = new AppConfig();
        var defaultJson = JsonSerializer.Serialize(defaultConfig, SerializerOptions);
        var defaultNode = JsonNode.Parse(defaultJson)!;

        DeepMerge(defaultNode.AsObject(), userNode.AsObject());

        return defaultNode.Deserialize<AppConfig>(SerializerOptions) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var tmpPath = _configPath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, _configPath, overwrite: true);
    }

    private static void DeepMerge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceObj && target[key] is JsonObject targetObj)
            {
                DeepMerge(targetObj, sourceObj);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}
