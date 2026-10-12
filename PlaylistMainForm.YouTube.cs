using System.Diagnostics;
using System.Text.Json;
using CatLuYNet.Core;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
    private async Task<Track> ReadTrackAsync(string url)
    {
        using var document = JsonDocument.Parse(await RunYtDlpAsync(lifetime.Token, "--no-playlist", "--dump-single-json", YouTubeUrl.CleanVideoUrl(url)));
        return ToTrack(document.RootElement);
    }

    private async Task<List<Track>> ReadPlaylistAsync(string url)
    {
        var output = await RunYtDlpAsync(lifetime.Token, "--flat-playlist", "--dump-json", url.Trim());
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { using var document = JsonDocument.Parse(line); return ToTrack(document.RootElement); }).Where(track => YouTubeUrl.IsVideoId(track.Id)).ToList();
    }

    private async Task CheckLinkedPlaylistsAsync()
    {
        var due = playlists.Where(playlist => playlist.SourceUrl is not null && DateTime.UtcNow - playlist.LastCheckedUtc >= TimeSpan.FromDays(1)).ToList();
        if (due.Count == 0) return;
        status.Text = "Проверка обновлений плейлистов...";
        var checkedCount = 0;
        var addedCount = 0;
        var failedCount = 0;
        foreach (var playlist in due)
        {
            try
            {
                var latestTracks = await ReadPlaylistAsync(playlist.SourceUrl!);
                addedCount += PlaylistStore.PrependMissingTracks(playlist, latestTracks);
                playlist.LastCheckedUtc = DateTime.UtcNow;
                checkedCount++;
            }
            catch (OperationCanceledException) when (closing) { return; }
            catch (Exception) { failedCount++; }
        }
        if (checkedCount > 0)
        {
            if (!SaveLibrary()) return;
            RenderPlaylists();
            status.Text = $"Плейлисты проверены; новых песен: {addedCount}";
        }
        else status.Text = "Не удалось проверить плейлисты YouTube";
        if (failedCount > 0 && checkedCount > 0) status.Text += $"; ошибок: {failedCount}";
    }

    private static Track ToTrack(JsonElement value) => new() { Id = value.GetProperty("id").GetString() ?? "", Name = value.GetProperty("title").GetString() ?? "Без названия", Artist = value.TryGetProperty("uploader", out var author) ? author.GetString() ?? "YouTube" : "YouTube" };
    private static bool IsYouTubeUrl(string value, out bool playlist) => YouTubeUrl.IsValid(value, out playlist);
    private static string CleanVideoUrl(string value) => YouTubeUrl.CleanVideoUrl(value);
    private static async Task<string> RunYtDlpAsync(CancellationToken token, params string[] arguments)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
        var start = new ProcessStartInfo(file) { WorkingDirectory = AppContext.BaseDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment.Remove("PYTHONHOME");
        start.Environment.Remove("PYTHONPATH");
        start.Environment.Remove("YTDLP_CONFIG");
        start.Environment["PATH"] = Environment.SystemDirectory;
        start.ArgumentList.Add("--ignore-config");
        start.ArgumentList.Add("--no-cache-dir");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        (string Output, string Error, int ExitCode) result;
        try { result = await ReadProcessAsync(start, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("yt-dlp не завершил запрос за 5 минут.");
        }
        if (result.ExitCode != 0)
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatLuYNet", "yt-dlp-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, result.Error);
            throw new InvalidOperationException(result.Error.Trim());
        }
        return result.Output;
    }

    private static async Task<(string Output, string Error, int ExitCode)> ReadProcessAsync(ProcessStartInfo start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить процесс.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await StopProcessAndDrainAsync(process, output, error).ConfigureAwait(false);
            throw;
        }
        await Task.WhenAll(output, error).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return (output.Result, error.Result, process.ExitCode);
    }

    private static async Task StopProcessAndDrainAsync(Process process, Task<string> output, Task<string> error)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(output, error).ConfigureAwait(false);
    }

    private async Task CheckForUpdateAsync(bool showResult)
    {
        if (checkingUpdate) return;
        checkingUpdate = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Maksimasz/CatLuYNet/releases/latest");
            request.Headers.UserAgent.ParseAdd("CatLuYNet/1.0");
            using var response = await http.SendAsync(request, lifetime.Token);
            response.EnsureSuccessStatusCode();
            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (closing) return;
            var tag = release.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "0.0.0";
            var newest = Version.Parse(tag);
            var current = typeof(PlaylistMainForm).Assembly.GetName().Version ?? new Version(0, 0);
            if (newest > current)
            {
                var asset = FindUpdateAsset(release.RootElement);
                updateUrl = asset?.Url;
                updateIsInstaller = asset?.Installer == true;
                updateBadge!.Visible = updateUrl is not null;
                if (showResult && updateUrl is not null && MessageBox.Show($"Доступна версия {newest}. {(updateIsInstaller ? "Скачать установщик?" : "Скачать portable ZIP? Его нужно распаковать вручную.")}", T("updates"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(updateUrl) { UseShellExecute = true });
                else if (showResult && updateUrl is null)
                    MessageBox.Show($"Версия {newest} найдена, но релиз не содержит установщика или portable ZIP.", T("updates"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (showResult) MessageBox.Show(T("upToDate"), T("updates"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (showResult && !closing) MessageBox.Show($"{T("updateError")}: {Short(ex.Message)}", T("updates"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { checkingUpdate = false; }
    }

    private static (string Url, bool Installer)? FindUpdateAsset(JsonElement release)
    {
        if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        (string Url, bool Installer)? portable = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!TryGetUpdateAsset(asset, out var url, out var installer)) continue;
            if (installer) return (url, true);
            portable ??= (url, false);
        }
        return portable;
    }

    private static bool TryGetUpdateAsset(JsonElement asset, out string url, out bool installer)
    {
        url = "";
        installer = false;
        if (asset.ValueKind != JsonValueKind.Object ||
            !asset.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String) return false;
        var name = nameElement.GetString()!;
        installer = name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase);
        if (!installer && !name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)) return false;
        if (!asset.TryGetProperty("browser_download_url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String) return false;
        url = urlElement.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(url);
    }

}
