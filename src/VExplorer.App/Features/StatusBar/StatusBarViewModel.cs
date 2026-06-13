using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using R3;
using VExplorer.App.Features.FileList;
using VExplorer.Core.Modes;
using VExplorer.Core.State;

namespace VExplorer.App.Features.StatusBar;

public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private readonly FileListViewModel _fileList;
    private readonly IDisposable _modeSubscription;
    private readonly IDisposable _statusSubscription;
    private readonly IDisposable _selectionSubscription;

    [ObservableProperty]
    private string _modeLabel = "NORMAL";

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>True when the current message is an error (shown in red).</summary>
    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private int _itemCount;

    [ObservableProperty]
    private int _selectionCount;

    public StatusBarViewModel(TabState tabState, FileListViewModel fileList)
    {
        _fileList = fileList;

        _modeSubscription = tabState
            .Mode.ObserveOnCurrentDispatcher()
            .Subscribe(mode => ModeLabel = FormatMode(mode));

        _statusSubscription = tabState
            .StatusMessage.ObserveOnCurrentDispatcher()
            .Subscribe(msg =>
            {
                StatusText = msg.Text;
                IsError = msg.IsError;
            });

        _selectionSubscription = tabState
            .Selection.ObserveOnCurrentDispatcher()
            .Subscribe(selection => SelectionCount = selection.Count);

        // Item count tracks DisplayItems (excluding the ".." parent entry).
        fileList.PropertyChanged += OnFileListChanged;
        UpdateItemCount();
    }

    /// <summary>
    /// Count summary. Plain "120 items"; under FILTER "12/120 matched"; with SEARCH
    /// matches "… · match 3/8"; a ", 3 selected" clause is appended when applicable.
    /// </summary>
    public string CountDisplay
    {
        get
        {
            string text = _fileList.IsFiltered
                ? $"{ItemCount}/{_fileList.TotalItemCount} matched"
                : $"{ItemCount} items";

            if (_fileList.SearchMatchCount > 0)
            {
                text +=
                    _fileList.SearchMatchOrdinal > 0
                        ? $" · match {_fileList.SearchMatchOrdinal}/{_fileList.SearchMatchCount}"
                        : $" · {_fileList.SearchMatchCount} matched";
            }

            if (SelectionCount > 0)
            {
                text += $", {SelectionCount} selected";
            }
            return text;
        }
    }

    private void OnFileListChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FileListViewModel.DisplayItems):
                UpdateItemCount();
                break;
            case nameof(FileListViewModel.TotalItemCount):
            case nameof(FileListViewModel.IsFiltered):
            case nameof(FileListViewModel.SearchMatchCount):
            case nameof(FileListViewModel.SearchMatchOrdinal):
                OnPropertyChanged(nameof(CountDisplay));
                break;
        }
    }

    private void UpdateItemCount()
    {
        int count = 0;
        foreach (FileItemRow row in _fileList.DisplayItems)
        {
            if (!row.IsParentEntry)
            {
                count++;
            }
        }
        ItemCount = count;
    }

    partial void OnItemCountChanged(int value) => OnPropertyChanged(nameof(CountDisplay));

    partial void OnSelectionCountChanged(int value) => OnPropertyChanged(nameof(CountDisplay));

    private static string FormatMode(Mode mode)
    {
        return mode switch
        {
            Mode.Normal => "NORMAL",
            Mode.Visual => "VISUAL",
            Mode.Search => "SEARCH",
            Mode.Filter => "FILTER",
            Mode.Command => "COMMAND",
            Mode.Address => "ADDRESS",
            Mode.Menu => "MENU",
            _ => mode.GetType().Name.ToUpperInvariant(),
        };
    }

    public void Dispose()
    {
        _fileList.PropertyChanged -= OnFileListChanged;
        _modeSubscription.Dispose();
        _statusSubscription.Dispose();
        _selectionSubscription.Dispose();
    }
}
