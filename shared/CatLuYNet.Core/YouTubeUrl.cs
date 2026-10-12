namespace CatLuYNet.Core;

public static class YouTubeUrl
{
    public static bool IsValid(string value, out bool playlist)
    {
        playlist = false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return false;
        if (!IsHttpUri(uri)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !IsYouTubeHost(uri.Host)) return false;
        var id = GetVideoId(uri);
        if (id is not null) return IsVideoId(id);

        playlist = IsPlaylistUri(uri);
        return playlist;
    }

    private static bool IsHttpUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsPlaylistUri(Uri uri)
    {
        var list = GetQueryValue(uri, "list");
        var path = uri.AbsolutePath.TrimEnd('/');
        var isPlaylistPath = path.Equals("/playlist", StringComparison.OrdinalIgnoreCase) || path.Equals("/watch", StringComparison.OrdinalIgnoreCase);
        var hasValidList = list is { Length: > 0 } && list.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
        return isPlaylistPath && hasValidList;
    }

    private static bool IsYouTubeHost(string host) => host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);

    public static bool IsVideoId(string? id) => id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    public static string? GetPlaylistId(string value) => Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ? GetQueryValue(uri, "list") : null;
    public static string CleanVideoUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return value;
        var id = GetVideoId(uri);
        return string.IsNullOrWhiteSpace(id) ? value : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}";
    }
    private static string? GetVideoId(Uri uri)
    {
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) return uri.AbsolutePath.Trim('/');
        var queryId = GetQueryValue(uri, "v");
        if (!string.IsNullOrWhiteSpace(queryId)) return queryId;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        return parts.Length == 2 && (parts[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("embed", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("live", StringComparison.OrdinalIgnoreCase)) ? parts[1] : null;
    }
    private static string? GetQueryValue(Uri uri, string name) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)).FirstOrDefault(part => part.Length == 2 && part[0].Equals(name, StringComparison.OrdinalIgnoreCase)) is { } pair ? Uri.UnescapeDataString(pair[1]) : null;
}
