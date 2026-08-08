using Microsoft.UI.Xaml.Controls;

namespace Shionji.App.WinUI;

/// <summary>右ペインの詳細表示。DataContext に ConfigDetailViewModel を受ける。</summary>
public sealed partial class ConfigDetailView : UserControl
{
    public ConfigDetailView()
    {
        InitializeComponent();
    }
}
