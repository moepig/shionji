using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Shionji.Presentation;

namespace Shionji.App.WinUI;

/// <summary>状態ドットの色。灰 = 未接続、青 = 処理中、緑 = 確立、赤 = 失敗。</summary>
public sealed partial class StatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Gray = new(Colors.Gray);
    private static readonly SolidColorBrush Blue = new(Colors.DodgerBlue);
    private static readonly SolidColorBrush Green = new(Colors.MediumSeaGreen);
    private static readonly SolidColorBrush Red = new(Colors.IndianRed);

    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        StatusKind.Busy => Blue,
        StatusKind.Connected => Green,
        StatusKind.Failed => Red,
        _ => Gray,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility。parameter="Invert" で反転。</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is true;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>null / 空文字 → Collapsed。</summary>
public sealed partial class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>
/// enum ⇔ ComboBox.SelectedIndex。parameter に enum 名を指定する
/// (DestinationKind / GatewayKind / CacheRole / AuroraRole)。項目順は enum 定義順。
/// </summary>
public sealed partial class EnumIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null ? 0 : (int)value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var index = value is int i ? i : 0;
        return (parameter as string) switch
        {
            "DestinationKind" => (DestinationKind)index,
            "GatewayKind" => (GatewayKind)index,
            "CacheRole" => (Domain.Configuration.CacheEndpointRole)index,
            "AuroraRole" => (Domain.Configuration.AuroraEndpointRole)index,
            _ => index,
        };
    }
}

/// <summary>詳細ペインの中身 (詳細 / エディタ) を選ぶ。</summary>
public sealed partial class DetailTemplateSelector : Microsoft.UI.Xaml.Controls.DataTemplateSelector
{
    public DataTemplate? Detail { get; set; }
    public DataTemplate? Editor { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        ConfigDetailViewModel => Detail,
        ConfigEditorViewModel => Editor,
        _ => null,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
