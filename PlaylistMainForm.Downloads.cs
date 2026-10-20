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
        var folders = new[] { settings.DownloadFolder }.Concat(playlists.Where(p => p.Tracks.Any(t => t.Id == track.Id))
            .Select(p => PlaylistDownloadFolder(settings.DownloadFolder, p.Name)));
        return folders.SelectMany(folder => new[] { Path.Combine(folder, Mp3FileName(track)), Path.Combine(folder, track.Id + ".mp3") })
            .FirstOrDefault(File.Exists);
    }

    private static string Mp3FileName(Track track)
    {
        if (!YouTubeUrl.IsVideoId(track.Id)) throw new ArgumentException("Некорректный ID трека.");
        var title = string.IsNullOrWhiteSpace(track.Name) ? "Трек" : track.Name;
        var safeTitle = PlaylistFileName(title[..Math.Min(title.Length, 140)])[..^5];
        return $"{safeTitle} [{track.Id}].mp3";
    }

    private void DeleteLocalAudio(Track track)
    {
        var path = LocalAudio(track);
        if (path is null) return;
        if (currentTrack?.Id == track.Id || incomingTrack?.Id == track.Id) Stop();
        File.Delete(path);
        foreach (var item in playlists.SelectMany(p => p.Tracks).Append(track))
            if (string.Equals(item.LocalFile, path, StringComparison.OrdinalIgnoreCase)) item.LocalFile = null;
        if (!SaveLibrary()) throw new IOException("MP3 удалён, но библиотеку сохранить не удалось.");
        RenderPlaylists();
        status.Text = "MP3 удалён с диска. Для воспроизведения будет использован YouTube.";
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
            finally { finished = true; if (!closing) { RenderPlaylists(); UpdateMiniPlayer(); status.Text = $"MP3 сохранено: {saved}; ошибок: {failed.Count} · {folder}"; } dialog.Close(); }
        };
        dialog.ShowDialog(this);
        return Task.CompletedTask;
    }

    private static void AddWindowsMp3Tags(string path, string youtubeUrl)
    {
        // Проводник читает URL и звёзды из WOAR и POPM, которые FFmpeg не записывает.
        using var frames = new MemoryStream();
        void Frame(string id, byte[] payload)
        {
            var frameHeader = new byte[10];
            System.Text.Encoding.ASCII.GetBytes(id).CopyTo(frameHeader, 0);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frameHeader.AsSpan(4, 4), payload.Length);
            frames.Write(frameHeader); frames.Write(payload);
        }
        Frame("WOAR", System.Text.Encoding.ASCII.GetBytes(youtubeUrl));
        Frame("POPM", [.. System.Text.Encoding.ASCII.GetBytes("Windows Media Player 9 Series\0"), 255, 0, 0, 0, 0]);
        var rewritten = path + ".tags";
        var created = false;
        try
        {
            using (var source = File.OpenRead(path))
            {
                var header = new byte[10];
                source.ReadExactly(header);
                if (!header.AsSpan(0, 3).SequenceEqual("ID3"u8) || header[3] != 3 || header[5] != 0 || header.AsSpan(6, 4).ContainsAnyInRange((byte)128, (byte)255))
                    throw new InvalidDataException("Ожидался MP3 с обычным тегом ID3v2.3.");
                var oldSize = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
                if (oldSize > source.Length - 10 || oldSize + frames.Length >= 1L << 28)
                    throw new InvalidDataException("Некорректный размер MP3-тегов.");
                var size = oldSize + (int)frames.Length;
                for (var i = 9; i >= 6; i--) { header[i] = (byte)(size & 127); size >>= 7; }
                using var output = new FileStream(rewritten, FileMode.CreateNew, FileAccess.Write);
                created = true;
                output.Write(header);
                frames.Position = 0;
                frames.CopyTo(output);
                source.CopyTo(output);
                output.Flush(true);
            }
            File.Move(rewritten, path, true);
        }
        finally { if (created && File.Exists(rewritten)) File.Delete(rewritten); }
    }

    private async Task DownloadTrackAsync(Track track, string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!YouTubeUrl.IsVideoId(track.Id)) throw new InvalidOperationException("Некорректный ID трека.");
        var legacy = Path.Combine(folder, track.Id + ".mp3");
        var destination = File.Exists(legacy) ? legacy : Path.Combine(folder, Mp3FileName(track));
        if (!File.Exists(destination))
        {
            var tools = Path.Combine(AppContext.BaseDirectory, "tools");
            if (!File.Exists(Path.Combine(tools, "ffmpeg.exe")) || !File.Exists(Path.Combine(tools, "ffprobe.exe")))
                throw new FileNotFoundException("Для MP3 нужны tools/ffmpeg.exe и tools/ffprobe.exe.");
            var temporary = Path.Combine(folder, ".ynet-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            try
            {
                await RunYtDlpWithTimeoutAsync(token, TimeSpan.FromHours(2), "--no-playlist", "-f", "bestaudio/best", "-x", "--audio-format", "mp3", "--audio-quality", settings.Mp3Quality,
                    "--embed-metadata", "--embed-thumbnail", "--convert-thumbnails", "jpg",
                    "--parse-metadata", "%(album_artist,artist,creator,uploader|)s:%(meta_album_artist)s",
                    "--parse-metadata", "%(webpage_url)s\n%(channel_url,uploader_url|)s\n%(description|)s:(?s)(?P<meta_comment>.+)",
                    "--ffmpeg-location", tools, "--no-progress", "-o", Path.Combine(temporary.Replace("%", "%%"), "audio.%(ext)s"), $"https://www.youtube.com/watch?v={track.Id}");
                token.ThrowIfCancellationRequested();
                var result = Path.Combine(temporary, "audio.mp3");
                if (!File.Exists(result) || new FileInfo(result).Length == 0) throw new IOException("MP3 не создан.");
                AddWindowsMp3Tags(result, $"https://www.youtube.com/watch?v={track.Id}");
                File.Move(result, destination, false);
            }
            finally { try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        track.LocalFile = destination;
        foreach (var duplicate in playlists.SelectMany(p => p.Tracks).Where(t => t.Id == track.Id)) duplicate.LocalFile = destination;
        if (!SaveLibrary()) throw new IOException("MP3 сохранён, но библиотеку сохранить не удалось.");
    }
}
