using System.Collections.Immutable;

namespace VExplorer.Core.State;

/// <summary>The outcome of applying a <c>:set</c> argument to a <see cref="Settings"/>.</summary>
/// <param name="Updated">The new settings when something changed; null for a query/error.</param>
/// <param name="Message">Status text (a value readout, an error, or empty).</param>
/// <param name="IsError">True when <paramref name="Message"/> describes a failure.</param>
public readonly record struct SetResult(Settings? Updated, string Message, bool IsError)
{
    public static SetResult Error(string message)
    {
        return new(null, message, true);
    }

    public static SetResult Query(string message)
    {
        return new(null, message, false);
    }

    public static SetResult Changed(Settings updated, string message = "")
    {
        return new(updated, message, false);
    }
}

/// <summary>
/// Pure parser/applier for the <c>:set</c> command (Vim-style). Lives in Core so
/// the syntax (<c>opt</c> / <c>noopt</c> / <c>opt!</c> / <c>opt?</c> /
/// <c>key=value</c> / <c>key?</c>) is unit-testable without UI or persistence.
/// </summary>
public static class SettingsCommand
{
    private static readonly Dictionary<
        string,
        (Func<Settings, bool> Get, Func<Settings, bool, Settings> Set)
    > Bools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hidden"] = (s => s.ShowHidden, (s, v) => s with { ShowHidden = v }),
        ["systemfiles"] = (s => s.ShowSystemFiles, (s, v) => s with { ShowSystemFiles = v }),
        ["ads"] = (s => s.ShowAds, (s, v) => s with { ShowAds = v }),
        ["foldersfirst"] = (s => s.FoldersFirst, (s, v) => s with { FoldersFirst = v }),
        ["fuzzy"] = (s => s.Fuzzy, (s, v) => s with { Fuzzy = v }),
    };

    private static readonly string[] ValueKeys =
    [
        "columns",
        "tree_follow_delay_ms",
        "incr_search_delay_ms",
        "address_bar_delay_ms",
    ];

    private static readonly string[] ValidColumns = ["name", "size", "mtime", "type"];

    /// <summary>All option/key names (for completion and help).</summary>
    public static IReadOnlyList<string> OptionNames =>
        [.. Bools.Keys.Order(StringComparer.OrdinalIgnoreCase), .. ValueKeys];

    /// <summary>
    /// Applies a whole <c>:set</c> argument string (possibly several space-separated
    /// options) to <paramref name="current"/>. A query (<c>?</c>) anywhere short-
    /// circuits and returns the readout. Any error short-circuits and reports it.
    /// </summary>
    public static SetResult Apply(Settings current, string arg)
    {
        string[] tokens = arg.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (tokens.Length == 0)
        {
            return SetResult.Error("set: usage :set OPTION (Tab to list options)");
        }

        Settings settings = current;
        bool changed = false;
        foreach (string token in tokens)
        {
            SetResult step = ApplyOne(settings, token);
            if (step.IsError)
            {
                return step;
            }
            if (step.Updated is Settings next)
            {
                settings = next;
                changed = true;
            }
            else
            {
                // A query token: report it directly (don't mix with bulk changes).
                return step;
            }
        }
        return changed ? SetResult.Changed(settings) : SetResult.Query("");
    }

    private static SetResult ApplyOne(Settings settings, string token)
    {
        // key=value
        int eq = token.IndexOf('=');
        if (eq > 0)
        {
            return ApplyValue(settings, token[..eq], token[(eq + 1)..]);
        }

        // key? — query
        if (token.EndsWith('?'))
        {
            return QueryOption(settings, token[..^1]);
        }

        // opt! — toggle
        if (token.EndsWith('!'))
        {
            string name = token[..^1];
            if (Bools.TryGetValue(name, out var b))
            {
                return SetResult.Changed(b.Set(settings, !b.Get(settings)));
            }
            return SetResult.Error($"set: not a boolean option: {name}");
        }

        // noOPT — off
        if (token.StartsWith("no", StringComparison.OrdinalIgnoreCase))
        {
            string name = token[2..];
            if (Bools.TryGetValue(name, out var b))
            {
                return SetResult.Changed(b.Set(settings, false));
            }
        }

        // bare boolean — on
        if (Bools.TryGetValue(token, out var on))
        {
            return SetResult.Changed(on.Set(settings, true));
        }

        return SetResult.Error($"set: unknown option: {token}");
    }

    private static SetResult QueryOption(Settings settings, string name)
    {
        if (Bools.TryGetValue(name, out var b))
        {
            return SetResult.Query($"{name}={(b.Get(settings) ? "on" : "off")}");
        }
        return name switch
        {
            "columns" => SetResult.Query($"columns={string.Join(',', settings.Columns)}"),
            "tree_follow_delay_ms" => SetResult.Query(
                $"tree_follow_delay_ms={settings.TreeFollowDebounceMs}"
            ),
            "incr_search_delay_ms" => SetResult.Query(
                $"incr_search_delay_ms={settings.IncrSearchDelayMs}"
            ),
            "address_bar_delay_ms" => SetResult.Query(
                $"address_bar_delay_ms={settings.AddressBarDelayMs}"
            ),
            _ => SetResult.Error($"set: unknown option: {name}"),
        };
    }

    private static SetResult ApplyValue(Settings settings, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "columns":
            {
                string[] cols =
                [
                    .. value.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    ),
                ];
                if (cols.Length == 0)
                {
                    return SetResult.Error("set: columns needs at least one column");
                }
                foreach (string c in cols)
                {
                    if (!ValidColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
                    {
                        return SetResult.Error(
                            $"set: unknown column '{c}' (valid: {string.Join(',', ValidColumns)})"
                        );
                    }
                }
                return SetResult.Changed(
                    settings with
                    {
                        Columns = cols.Select(c => c.ToLowerInvariant()).ToImmutableArray(),
                    }
                );
            }
            case "tree_follow_delay_ms":
                return ApplyInt(value, key, ms => settings with { TreeFollowDebounceMs = ms });
            case "incr_search_delay_ms":
                return ApplyInt(value, key, ms => settings with { IncrSearchDelayMs = ms });
            case "address_bar_delay_ms":
                return ApplyInt(value, key, ms => settings with { AddressBarDelayMs = ms });
            default:
                return SetResult.Error($"set: unknown option: {key}");
        }
    }

    private static SetResult ApplyInt(string value, string key, Func<int, Settings> apply)
    {
        if (!int.TryParse(value, out int n) || n < 0)
        {
            return SetResult.Error($"set: {key} expects a non-negative integer");
        }
        return SetResult.Changed(apply(n));
    }
}
