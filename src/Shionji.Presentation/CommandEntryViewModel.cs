using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shionji.Presentation;

/// <summary>接続先設定ウィンドウで編集するコマンド 1 件の入力行。</summary>
public sealed partial class CommandEntryViewModel(Action<CommandEntryViewModel> remove) : ObservableObject
{
    /// <summary>ボタンに出す名前。空ならコマンドがそのまま名前になる。</summary>
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CommandLine { get; set; } = string.Empty;

    /// <summary>名前もコマンドも未入力の行は保存時に無視する。</summary>
    public bool IsBlank => Label.Trim().Length == 0 && CommandLine.Trim().Length == 0;

    [RelayCommand]
    private void Remove() => remove(this);
}
