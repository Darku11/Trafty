using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Trafty.App.Services;
using Trafty.App.ViewModels;

namespace Trafty.App.Views;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await LoadAsync();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await LoadAsync();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async Task LoadAsync()
    {
        var scroll = this.FindControl<ScrollViewer>("ChangelogScroll")!;
        var list = this.FindControl<ItemsControl>("ChangelogList")!;
        var status = this.FindControl<TextBlock>("StatusText")!;

        scroll.IsVisible = false;
        status.IsVisible = true;
        status.Text = "Loading…";
        list.ItemsSource = null;

        try
        {
            IReadOnlyList<ChangelogEntry> entries = await GitHubChangelogService.FetchRecentCommitsAsync();

            if (entries.Count == 0)
            {
                status.Text = "No commits found.";
                return;
            }

            IReadOnlyList<ChangelogRow> rows = entries
                .Select(entry => new ChangelogRow(entry.Message, entry.ShortSha, entry.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))
                .ToList();

            list.ItemsSource = rows;

            status.IsVisible = false;
            scroll.IsVisible = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            status.Text = $"Could not reach GitHub: {ex.Message}";
        }
    }
}
