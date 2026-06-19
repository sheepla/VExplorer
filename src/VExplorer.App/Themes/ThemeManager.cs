using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using R3;
using VExplorer.App.Settings;
using VExplorer.Core.State;
using ThemeMode = VExplorer.Core.State.ThemeMode;

namespace VExplorer.App.Themes;

/// <summary>
/// Owns the live colour theme. Resolves the effective light/dark appearance from
/// <see cref="Settings.Theme"/> (following the Windows app theme when set to
/// <see cref="ThemeMode.System"/>) and swaps the merged theme dictionary at
/// runtime. Both the toggle button and <c>:set theme=</c> flow through
/// <see cref="AppState.SettingsChanged"/>, so this is the single apply point.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private static readonly Uri LightUri = new("Themes/Theme.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Themes/Theme.Dark.xaml", UriKind.Relative);

    private readonly AppState _appState;
    private readonly SettingsStore _settingsStore;
    private readonly ReactiveProperty<bool> _isDark = new(false);
    private readonly IDisposable _subscription;

    private ResourceDictionary? _current;
    private bool? _appliedDark;

    public ThemeManager(AppState appState, SettingsStore settingsStore)
    {
        _appState = appState;
        _settingsStore = settingsStore;

        // SettingsChanged emits the current value immediately, applying the
        // initial theme, then re-applies whenever the effective appearance changes.
        _subscription = appState
            .SettingsChanged.ObserveOnCurrentDispatcher()
            .Subscribe(settings => Apply(ResolveDark(settings.Theme)));
    }

    /// <summary>True when the dark palette is active. Drives the toggle icon.</summary>
    public bool IsDark => _isDark.Value;

    /// <summary>Emits the current value and every subsequent dark/light change.</summary>
    public Observable<bool> IsDarkChanged => _isDark;

    /// <summary>Flips between explicit light and dark, persisting the choice.</summary>
    public void Toggle()
    {
        ThemeMode target = _isDark.Value ? ThemeMode.Light : ThemeMode.Dark;
        Core.State.Settings updated = _appState.Settings with { Theme = target };
        _appState.UpdateSettings(updated);
        _settingsStore.Save(updated);
    }

    private static bool ResolveDark(ThemeMode mode)
    {
        return mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => WindowsPrefersDark(),
        };
    }

    private static bool WindowsPrefersDark()
    {
        // AppsUseLightTheme: 0 = dark, 1 (or absent) = light.
        object? value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme",
            1
        );
        return value is int i && i == 0;
    }

    private void Apply(bool dark)
    {
        if (_appliedDark == dark)
        {
            return;
        }

        ResourceDictionary next = new() { Source = dark ? DarkUri : LightUri };
        Collection<ResourceDictionary> merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null)
        {
            merged.Remove(_current);
        }
        merged.Add(next);
        _current = next;
        _appliedDark = dark;
        _isDark.Value = dark;
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _isDark.Dispose();
    }
}
