using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YouTubeRadio;

internal static class YouTubeMetadata
{
    private static readonly HttpClient Http = new();
    private static readonly Regex VideoIds = new("\\\"videoId\\\":\\\"(?<id>[A-Za-z0-9_-]{11})\\\"", RegexOptions.Compiled);

    static YouTubeMetadata() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

    public static async Task<VideoMetadata> GetVideoAsync(string url, string id)
    {
        var json = await Http.GetStringAsync($"https://www.youtube.com/oembed?format=json&url={Uri.EscapeDataString(url)}");
        var data = JsonSerializer.Deserialize<OEmbed>(json) ?? throw new InvalidOperationException("YouTube не вернул метаданные ролика.");
        return new VideoMetadata(id, data.Title, data.AuthorName);
    }

    public static async Task<List<VideoMetadata>> GetPlaylistVideosAsync(string url)
    {
        var html = await Http.GetStringAsync(url);
        var ids = VideoIds.Matches(html).Select(x => x.Groups["id"].Value).Distinct().ToList();
        var videos = new List<VideoMetadata>();
        foreach (var id in ids)
        {
            try { videos.Add(await GetVideoAsync($"https://www.youtube.com/watch?v={id}", id)); }
            catch (HttpRequestException) { }
        }
        if (videos.Count == 0) throw new InvalidOperationException("Не удалось прочитать песни плейлиста.");
        return videos;
    }

    private sealed class OEmbed
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("author_name")]
        public string AuthorName { get; set; } = "";
    }
}

internal sealed record VideoMetadata(string Id, string Title, string Author);

