using System.Collections.Immutable;
using System.IO;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;
using VExplorer.Core.State;

namespace VExplorer.App.Settings;

public sealed class SettingsStore(ILogger<SettingsStore> logger)
{
    private readonly ILogger<SettingsStore> _logger = logger;

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VExplorer",
        "config.toml"
    );

    private const string DefaultToml = """
        # VExplorer configuration
        show_hidden = false
        show_system_files = false
        show_extensions = true
        show_ads = false
        folders_first = true
        fuzzy = false
        theme = "system"
        tree_follow_delay_ms = 200
        incr_search_delay_ms = 150
        address_bar_delay_ms = 100
        list_timeout_ms = 5000
        filter_delay_ms = 150
        tree_children_cap = 200
        columns = ["name", "size", "mtime", "type"]
        """;

    public Core.State.Settings Load()
    {
        if (!File.Exists(_path))
        {
            string? dir = Path.GetDirectoryName(_path);
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_path, DefaultToml);
        }

        try
        {
            string text = File.ReadAllText(_path);
            TomlTable model = Toml.ToModel(text);
            return new Core.State.Settings
            {
                ShowHidden = GetBool(model, "show_hidden", false),
                ShowSystemFiles = GetBool(model, "show_system_files", false),
                ShowExtensions = GetBool(model, "show_extensions", true),
                ShowAds = GetBool(model, "show_ads", false),
                FoldersFirst = GetBool(model, "folders_first", true),
                Fuzzy = GetBool(model, "fuzzy", false),
                Theme = GetTheme(model, "theme", ThemeMode.System),
                TreeFollowDebounceMs = GetInt(model, "tree_follow_delay_ms", 200),
                IncrSearchDelayMs = GetInt(model, "incr_search_delay_ms", 150),
                AddressBarDelayMs = GetInt(model, "address_bar_delay_ms", 100),
                ListTimeoutMs = GetInt(model, "list_timeout_ms", 5000),
                FilterDelayMs = GetInt(model, "filter_delay_ms", 150),
                TreeChildrenCap = GetInt(model, "tree_children_cap", 200),
                Columns = GetStringArray(model, "columns", ["name", "size", "mtime", "type"]),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings parse failed for {Path}; using defaults", _path);
            return Core.State.Settings.Default;
        }
    }

    public void Save(Core.State.Settings settings)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        TomlArray colArray = [];
        foreach (string col in settings.Columns)
        {
            colArray.Add(col);
        }

        TomlTable model = new()
        {
            ["show_hidden"] = settings.ShowHidden,
            ["show_system_files"] = settings.ShowSystemFiles,
            ["show_extensions"] = settings.ShowExtensions,
            ["show_ads"] = settings.ShowAds,
            ["folders_first"] = settings.FoldersFirst,
            ["fuzzy"] = settings.Fuzzy,
            ["theme"] = settings.Theme.ToString().ToLowerInvariant(),
            ["tree_follow_delay_ms"] = (long)settings.TreeFollowDebounceMs,
            ["incr_search_delay_ms"] = (long)settings.IncrSearchDelayMs,
            ["address_bar_delay_ms"] = (long)settings.AddressBarDelayMs,
            ["list_timeout_ms"] = (long)settings.ListTimeoutMs,
            ["filter_delay_ms"] = (long)settings.FilterDelayMs,
            ["tree_children_cap"] = (long)settings.TreeChildrenCap,
            ["columns"] = colArray,
        };
        File.WriteAllText(_path, Toml.FromModel(model));
    }

    private static bool GetBool(TomlTable model, string key, bool def)
    {
        return model.TryGetValue(key, out object? val) && val is bool b ? b : def;
    }

    private static int GetInt(TomlTable model, string key, int def)
    {
        return model.TryGetValue(key, out object? val) && val is long l ? (int)l : def;
    }

    private static ThemeMode GetTheme(TomlTable model, string key, ThemeMode def)
    {
        return
            model.TryGetValue(key, out object? val)
            && val is string s
            && Enum.TryParse(s, ignoreCase: true, out ThemeMode mode)
            ? mode
            : def;
    }

    private static ImmutableArray<string> GetStringArray(
        TomlTable model,
        string key,
        ImmutableArray<string> def
    )
    {
        if (!model.TryGetValue(key, out object? val) || val is not TomlArray arr)
        {
            return def;
        }

        string[] result = new string[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            result[i] = arr[i] as string ?? "";
        }
        return result.ToImmutableArray();
    }
}
