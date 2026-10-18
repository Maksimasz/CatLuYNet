using System.Text.Json;
using System.Linq;

namespace CatLuYNet.Core;

public static class PlaylistStore
{
    private sealed class Library { public required List<Playlist>? Playlists { get; set; } }

    public static List<Playlist> Load(string statePath, string oldStatePath)
    {
        var path = File.Exists(statePath) ? statePath : oldStatePath;
        if (!File.Exists(path)) return [];
        return Read(path);
    }

    public static List<Playlist> Import(string sourcePath, string statePath, List<Playlist> existing)
    {
        var imported = Read(sourcePath);
        if (imported.Count > 0) Save(statePath, [.. existing, .. imported]);
        return imported;
    }

    private static List<Playlist> Read(string path)
    {
        var json = File.ReadAllText(path);
        List<Playlist>? loaded;
        if (json.TrimStart().StartsWith('{')) loaded = JsonSerializer.Deserialize<Library>(json)?.Playlists;
        else
        {
            var tracks = JsonSerializer.Deserialize<List<Track>>(json);
            loaded = tracks is null ? null : [new Playlist { Name = "Сохранённые", Expanded = true, Tracks = tracks }];
        }
        if (loaded is null || loaded.Any(p => !IsValidPlaylist(p)))
            throw new JsonException("Некорректная структура библиотеки.");
        return loaded;
    }

    private static bool IsValidPlaylist(Playlist? playlist) => playlist is not null &&
        playlist.Name is not null && playlist.Tracks is not null &&
        playlist.Tracks.All(IsValidTrack) && IsValidSourceUrl(playlist.SourceUrl);

    private static bool IsValidTrack(Track? track) => track is not null &&
        YouTubeUrl.IsVideoId(track.Id) && track.Name is not null && track.Artist is not null;

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
