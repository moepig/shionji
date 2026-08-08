using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shionji.Presentation;

/// <summary>タグ条件 1 件分の入力行。並べた行はすべて満たす必要がある (AND)。</summary>
public sealed partial class TagEntryViewModel(Action<TagEntryViewModel> remove) : ObservableObject
{
    [ObservableProperty]
    public partial string Key { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>キーも値も未入力の行は保存時に無視する。</summary>
    public bool IsBlank => Key.Trim().Length == 0 && Value.Trim().Length == 0;

    [RelayCommand]
    private void Remove() => remove(this);
}
