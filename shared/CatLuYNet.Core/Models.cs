namespace CatLuYNet.Core;

public sealed class Playlist { public string Name { get; set; } = ""; public bool Expanded { get; set; } = true; public string? SourceUrl { get; set; } public DateTime LastCheckedUtc { get; set; } public List<Track> Tracks { get; set; } = []; }
public sealed class Track { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Artist { get; set; } = ""; public string? LocalFile { get; set; } public bool IsNew { get; set; } }
