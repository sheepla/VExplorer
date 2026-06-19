using R3;

namespace VExplorer.Core.State;

public sealed class AppState(Settings? initialSettings = null) : IDisposable
{
    private readonly ReactiveProperty<Guid> _activeTabId = new(Guid.Empty);
    private readonly ReactiveProperty<Settings> _settings = new(
        initialSettings ?? Settings.Default
    );

    public Observable<Guid> ActiveTabId => _activeTabId;
    public Guid ActiveTabIdValue => _activeTabId.Value;

    /// <summary>The current settings (latest value of <see cref="SettingsChanged"/>).</summary>
    public Settings Settings => _settings.Value;

    /// <summary>Emits the current settings and every subsequent change (e.g. via <c>:set</c>).</summary>
    public Observable<Settings> SettingsChanged => _settings;

    public void SetActiveTab(Guid tabId)
    {
        _activeTabId.Value = tabId;
    }

    public void UpdateSettings(Settings settings)
    {
        _settings.Value = settings;
    }

    public void Dispose()
    {
        _activeTabId.Dispose();
        _settings.Dispose();
    }
}
