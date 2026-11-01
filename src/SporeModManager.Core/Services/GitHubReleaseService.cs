using SporeModManager.Core.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SporeModManager.Core.Services;

public class GitHubReleaseService
{
    private readonly HttpClient _http;
    public GitHubReleaseService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SporeModManager/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<List<ModApiRelease>> ListReleasesAsync(string user, string repo, int perPage = 30, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{user}/{repo}/releases?per_page={perPage}";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var result = new List<ModApiRelease>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var r = new ModApiRelease
            {
                TagName = el.GetProperty("tag_name").GetString() ?? "",
                Name = el.TryGetProperty("name", out var n) && n.ValueKind != JsonValueKind.Null ? n.GetString() ?? "" : "",
                HtmlUrl = el.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
            };
            if (el.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(pa.GetString(), out var dt)) r.PublishedAt = dt;
            }
            if (el.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    r.Assets.Add(new ModApiAsset
                    {
                        Name = a.GetProperty("name").GetString() ?? "",
                        DownloadUrl = a.GetProperty("browser_download_url").GetString() ?? "",
                        Size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0
                    });
                }
            }
            result.Add(r);
        }
        return result;
    }

    public async Task<ModApiRelease?> GetLatestAsync(string user, string repo, CancellationToken ct = default)
    {
        var list = await ListReleasesAsync(user, repo, 1, ct);
        return list.Count > 0 ? list[0] : null;
    }
}