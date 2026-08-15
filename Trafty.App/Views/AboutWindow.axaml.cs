using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Trafty.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnChangelogClick(object? sender, RoutedEventArgs e) => await new ChangelogWindow().ShowDialog(this);
}
