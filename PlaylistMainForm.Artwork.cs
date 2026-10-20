using System.Diagnostics;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
    private readonly PictureBox trackArtwork = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Visible = false };
    private CancellationTokenSource? artworkRequest;
    private string? artworkKey;
    private int artworkVersion;

    private void StartDancingCat()
    {
        if (closing) return;
        var path = currentTrack is null ? null : LocalAudio(currentTrack);
        var key = path is null ? "" : path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        catVideoHost.Visible = true;
        if (key == artworkKey)
        {
            if (trackArtwork.Image is not null) { trackArtwork.Visible = true; trackArtwork.BringToFront(); }
            else PlayDancingCat();
            return;
        }
        ClearTrackArtwork();
        artworkKey = key;
        PlayDancingCat();
        if (path is null) return;
        artworkRequest = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = LoadTrackArtworkAsync(path, artworkVersion, artworkRequest.Token);
    }

    private void ClearTrackArtwork()
    {
        artworkVersion++;
        artworkRequest?.Cancel(); artworkRequest?.Dispose(); artworkRequest = null;
        artworkKey = null;
        trackArtwork.Visible = false;
        var previous = trackArtwork.Image;
        trackArtwork.Image = null;
        previous?.Dispose();
    }

    private async Task LoadTrackArtworkAsync(string path, int version, CancellationToken token)
    {
        Image? cover = null;
        try
        {
            cover = await ReadTrackArtworkAsync(path, token);
            if (cover is null || closing || token.IsCancellationRequested || version != artworkVersion) return;
            catPlayer.Stop();
            catMedia?.Dispose(); catMedia = null;
            trackArtwork.Image = cover; cover = null;
            catVideoHost.Visible = true;
            trackArtwork.Visible = true;
            trackArtwork.BringToFront();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or OutOfMemoryException) { }
        finally { cover?.Dispose(); }
    }

    private static async Task<Image?> ReadTrackArtworkAsync(string path, CancellationToken token)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "CatLuYNet-cover-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"))
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", path, "-map", "0:v:0", "-frames:v", "1", "-vf", "scale=512:512:force_original_aspect_ratio=decrease", temporary }) start.ArgumentList.Add(arg);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var result = await ReadProcessAsync(start, timeout.Token);
            if (result.ExitCode != 0 || !File.Exists(temporary)) return null;
            token.ThrowIfCancellationRequested();
            using var image = Image.FromFile(temporary);
            return new Bitmap(image);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
