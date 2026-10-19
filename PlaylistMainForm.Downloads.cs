using CatLuYNet.Core;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
    private readonly CheckBox downloadOnAdd = new() { Text = "Скачать MP3", AutoSize = true, Dock = DockStyle.Fill };
    private readonly SemaphoreSlim downloadGate = new(1, 1);

    private string? LocalAudio(Track track)
    {
        if (!YouTubeUrl.IsVideoId(track.Id)) return null;
        if (!string.IsNullOrWhiteSpace(track.LocalFile) && File.Exists(track.LocalFile)) return track.LocalFile;
        if (string.IsNullOrWhiteSpace(settings.DownloadFolder)) return null;
        var path = Path.Combine(settings.DownloadFolder, track.Id + ".mp3");
        if (File.Exists(path)) return path;
        return playlists.Where(p => p.Tracks.Any(t => t.Id == track.Id))
            .Select(p => Path.Combine(PlaylistDownloadFolder(settings.DownloadFolder, p.Name), track.Id + ".mp3"))
            .FirstOrDefault(File.Exists);
    }

    private string AudioSource(Track? track) => track is not null && LocalAudio(track) is not null ? "MP3 · с диска" : "YouTube";

    private string PlaybackSource() => media?.Mrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase) == true ? "MP3 · с диска" : "YouTube";

    private async Task<Uri> ResolveAudioAsync(Track track, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var local = LocalAudio(track);
        if (local is not null) return new Uri(Path.GetFullPath(local));
        var output = await RunYtDlpAsync(token, "--no-playlist", "-f", "bestaudio/best", "-g", $"https://www.youtube.com/watch?v={track.Id}");
        token.ThrowIfCancellationRequested();
        var url = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("yt-dlp не вернул поток.");
        return new Uri(url);
    }

    private static string PlaylistDownloadFolder(string root, string playlistName)
    {
        var safeName = PlaylistFileName(playlistName[..Math.Min(playlistName.Length, 120)])[..^5];
        return Path.Combine(Path.GetFullPath(root), safeName);
    }

    private Task DownloadTracksAsync(Playlist playlist, IEnumerable<Track> selection)
    {
        var selected = selection.DistinctBy(t => t.Id).ToList();
        if (selected.Count == 0) return Task.CompletedTask;
        using var picker = new FolderBrowserDialog
        {
            Description = $"Выберите место для папки плейлиста «{playlist.Name}»",
            UseDescriptionForTitle = true,
            SelectedPath = settings.DownloadFolder,
            ShowNewFolderButton = true
        };
        if (picker.ShowDialog(ActiveForm ?? this) != DialogResult.OK) return Task.CompletedTask;
        settings.DownloadFolder = picker.SelectedPath;
        if (!SaveSettings()) return Task.CompletedTask;
        var folder = PlaylistDownloadFolder(picker.SelectedPath, playlist.Name);
        using var dialog = new Form { Text = "Скачивание MP3", Size = new Size(600, 240), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false };
        var progress = new Label { Dock = DockStyle.Fill, Padding = new Padding(12), AutoEllipsis = true };
        var cancel = new System.Windows.Forms.Button { Text = "Отмена", Dock = DockStyle.Bottom, Height = 36 };
        var destination = new TextBox { Text = folder, ReadOnly = true, Dock = DockStyle.Top, Height = 55, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var destinationTitle = new Label { Text = "Папка скачивания:", Dock = DockStyle.Top, Height = 24 };
        dialog.Controls.Add(progress); dialog.Controls.Add(destination); dialog.Controls.Add(destinationTitle); dialog.Controls.Add(cancel);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        cancel.Click += (_, _) => cancellation.Cancel();
        bool finished = false;
        dialog.FormClosing += (_, e) => { if (!finished) { cancellation.Cancel(); e.Cancel = true; } };
        dialog.Shown += async (_, _) =>
        {
            var saved = 0; var failed = new List<string>();
            try
            {
                await downloadGate.WaitAsync(cancellation.Token);
                try
                {
                    Directory.CreateDirectory(folder);
                    foreach (var track in selected)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        progress.Text = $"{saved + failed.Count + 1} / {selected.Count}: {track.Name}";
                        try { await DownloadTrackAsync(track, folder, cancellation.Token); saved++; }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { failed.Add($"{track.Name}: {ex.Message}"); }
                    }
                }
                finally { downloadGate.Release(); }
                if (!closing && failed.Count > 0) MessageBox.Show(dialog, $"Сохранено: {saved}; ошибок: {failed.Count}\n" + string.Join("\n", failed.Take(5)), "Скачивание MP3", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closing) MessageBox.Show(dialog, ex.Message, "Ошибка скачивания", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { finished = true; if (!closing) { RenderPlaylists(); status.Text = $"MP3 сохранено: {saved}; ошибок: {failed.Count} · {folder}"; } dialog.Close(); }
        };
        dialog.ShowDialog(this);
        return Task.CompletedTask;
    }

    private async Task DownloadTrackAsync(Track track, string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!YouTubeUrl.IsVideoId(track.Id)) throw new InvalidOperationException("Некорректный ID трека.");
        var destination = Path.Combine(folder, track.Id + ".mp3");
        if (!File.Exists(destination))
        {
            var tools = Path.Combine(AppContext.BaseDirectory, "tools");
            if (!File.Exists(Path.Combine(tools, "ffmpeg.exe")) || !File.Exists(Path.Combine(tools, "ffprobe.exe")))
                throw new FileNotFoundException("Для MP3 нужны tools/ffmpeg.exe и tools/ffprobe.exe.");
            var temporary = Path.Combine(folder, ".ynet-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                await RunYtDlpWithTimeoutAsync(token, TimeSpan.FromHours(2), "--no-playlist", "-f", "bestaudio/best", "-x", "--audio-format", "mp3", "--audio-quality", settings.Mp3Quality, "--ffmpeg-location", tools, "--no-progress", "-o", Path.Combine(temporary.Replace("%", "%%"), "audio.%(ext)s"), $"https://www.youtube.com/watch?v={track.Id}");
                token.ThrowIfCancellationRequested();
                var result = Path.Combine(temporary, "audio.mp3");
                if (!File.Exists(result) || new FileInfo(result).Length == 0) throw new IOException("MP3 не создан.");
                File.Move(result, destination, false);
            }
            finally { try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        track.LocalFile = destination;
        foreach (var duplicate in playlists.SelectMany(p => p.Tracks).Where(t => t.Id == track.Id)) duplicate.LocalFile = destination;
        if (!SaveLibrary()) throw new IOException("MP3 сохранён, но библиотеку сохранить не удалось.");
    }
}
