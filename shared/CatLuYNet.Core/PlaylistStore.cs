using System.Text.Json;
using System.Linq;

namespace CatLuYNet.Core;

public static class PlaylistStore
{
    private sealed class Library { public List<Playlist>? Playlists { get; set; } = []; }

    public static List<Playlist> Load(string statePath, string oldStatePath)
    {
        var path = File.Exists(statePath) ? statePath : oldStatePath;
        if (!File.Exists(path)) return [];
        var json = File.ReadAllText(path);
        List<Playlist>? loaded;
        if (json.TrimStart().StartsWith('{')) loaded = JsonSerializer.Deserialize<Library>(json)?.Playlists;
        else
        {
            var tracks = JsonSerializer.Deserialize<List<Track>>(json);
            loaded = tracks is null ? null : [new Playlist { Name = "Сохранённые", Expanded = true, Tracks = tracks }];
        }
        if (loaded is null || loaded.Any(p => p is null || p.Name is null || p.Tracks is null || p.Tracks.Any(t => t is null || !YouTubeUrl.IsVideoId(t.Id) || t.Name is null || t.Artist is null) || !IsValidSourceUrl(p.SourceUrl)))
            throw new JsonException("Некорректная структура библиотеки.");
        return loaded;
    }

    public static int PrependMissingTracks(Playlist playlist, IEnumerable<Track> latestTracks)
    {
        var knownIds = playlist.Tracks.Select(track => track.Id).ToHashSet(StringComparer.Ordinal);
        var added = latestTracks.Where(track => knownIds.Add(track.Id)).ToList();
        foreach (var track in added) track.IsNew = true;
        playlist.Tracks.InsertRange(0, added);
        return added.Count;
    }

    private static bool IsValidSourceUrl(string? url)
    {
        if (url is null) return true;
        return YouTubeUrl.IsValid(url, out var isPlaylist) && isPlaylist;
    }

    public static void Save(string path, List<Playlist> playlists) => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(new Library { Playlists = playlists }));
}
