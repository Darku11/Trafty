using System.Net.Http;
using System.Text.Json;

namespace Trafty.App.Services;

public sealed record ChangelogEntry(string Message, DateTimeOffset Date, string ShortSha);

/// <summary>
/// Fetches recent commit messages from the Trafty GitHub repository's public REST API, so
/// the app can show a live changelog without shipping a maintained changelog file. Read-only,
/// unauthenticated (GitHub's anonymous rate limit is more than enough for a manual refresh).
/// </summary>
public static class GitHubChangelogService
{
    private const string CommitsUrl = "https://api.github.com/repos/Darku11/Trafty/commits?per_page=20";

    public static async Task<IReadOnlyList<ChangelogEntry>> FetchRecentCommitsAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Trafty-App");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using HttpResponseMessage response = await client.GetAsync(CommitsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var entries = new List<ChangelogEntry>();

        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            string sha = item.GetProperty("sha").GetString() ?? string.Empty;
            JsonElement commit = item.GetProperty("commit");
            string fullMessage = commit.GetProperty("message").GetString() ?? string.Empty;
            string firstLine = fullMessage.Split('\n', 2)[0];
            DateTimeOffset date = commit.GetProperty("author").GetProperty("date").GetDateTimeOffset();

            entries.Add(new ChangelogEntry(firstLine, date, sha.Length >= 7 ? sha[..7] : sha));
        }

        return entries;
    }
}
