using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shionji.Domain.Configuration;

namespace Shionji.Presentation;

/// <summary>詳細ペインに並ぶ、接続先設定に登録されたコマンドのボタン 1 個分。</summary>
public sealed partial class ExternalCommandViewModel(
    LaunchCommand definition,
    Action<LaunchCommand> run) : ObservableObject
{
    public LaunchCommand Definition { get; } = definition;

    public string Label => Definition.Label;

    /// <summary>登録された内容。プレースホルダを含んだまま出す。</summary>
    public string CommandLine => Definition.CommandLine;

    /// <summary>実行できる状態か。接続していないと差し込む値が決まらない。</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [RelayCommand]
    private void Run() => run(Definition);
}
