using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LibVLCSharp.Shared;
using YouTubeRadio;
using CatLuYNet.Core;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type FormType = typeof(PlaylistMainForm);
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "pipes")
        {
            Console.Error.Write(new string('e', 1_000_000));
            Console.Out.Write(new string('o', 1_000_000));
            return 0;
        }
        if (args.FirstOrDefault() == "wait")
        {
            File.WriteAllText(args[1], Environment.ProcessId.ToString());
            Thread.Sleep(60_000);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var directory = Path.Combine(Path.GetTempPath(), "ynet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestUrlsAndTime();
            TestReleaseAssets();
            Check(((string)Call(null, "FormatYtDlpError", "WARNING: initial data\nWARNING: runtime\nERROR: [youtube] id: Video unavailable")!).Contains("видео недоступно"), "download reports actual error after warnings");
            Check((string)Call(null, "FormatYtDlpError", "WARNING: warning\nERROR: conversion failed")! == "ERROR: conversion failed", "conversion error is preserved");
            Check(!string.IsNullOrWhiteSpace((string)Call(null, "FormatYtDlpError", "")!), "empty downloader error has explanation");
            TestLocalAudio(directory);
            TestEditorButtons(directory, args.Contains("--render-editor"));
            TestSharedCore(directory);
            TestStorage(directory);
            TestPlaylistTransfer(directory);
            TestPlaylistExportSelection(directory);
            TestPlaybackAndControls(directory);
            // No WinForms continuation is needed by the subprocess checks.
            SynchronizationContext.SetSynchronizationContext(null);
            TestProcessAsync(directory).GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {checks} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static object? Call(object? target, string method, params object?[] args)
    {
        var flags = target is null ? BindingFlags.Static | BindingFlags.NonPublic : PrivateInstance;
        try { return FormType.GetMethod(method, flags)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static object Get(object target, string name) => FormType.GetField(name, PrivateInstance)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => FormType.GetField(name, PrivateInstance)!.SetValue(target, value);
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        checks++;
    }
    private static PlaylistMainForm NewForm(string directory) => (PlaylistMainForm)Activator.CreateInstance(
        FormType, PrivateInstance, null, [directory, Path.Combine(directory, "legacy.json")], null)!;
    private static object Model(string name) => Activator.CreateInstance(name == "Playlist" ? typeof(Playlist) : typeof(Track))!;
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);

    private static void TestEditorButtons(string directory, bool render)
    {
        using var owner = NewForm(Path.Combine(directory, "editor-layout"));
        var type = FormType.GetNestedType("PlaylistEditorDialog", BindingFlags.NonPublic)!;
        using var editor = (Form)Activator.CreateInstance(type, owner, new Playlist { Name = "Мой плейлист" })!;
        editor.CreateControl();
        editor.PerformLayout();
        var grid = (TableLayoutPanel)editor.Controls[0];
        var actions = (TableLayoutPanel)grid.GetControlFromPosition(0, 3)!;
        Check(actions.RowCount == 3, "editor has three action rows");
        var firstRow = (TableLayoutPanel)actions.GetControlFromPosition(0, 0)!;
        Check(firstRow.GetControlFromPosition(3, 0)!.Text == "Удалить" && firstRow.GetControlFromPosition(4, 0)!.Text == "Удалить весь плейлист", "playlist delete buttons adjacent");
        var mp3Row = (TableLayoutPanel)actions.GetControlFromPosition(0, 1)!;
        Check(mp3Row.Controls.Count == 3, "MP3 download and delete share one row");
        Check(actions.GetControlFromPosition(0, 2)!.Text == "Закончить редактирование плейлиста", "finish editing has its own last row");
        editor.ShowInTaskbar = false;
        editor.StartPosition = FormStartPosition.Manual;
        editor.Location = new Point(-32000, -32000);
        editor.Show();
        foreach (var width in new[] { 500, 620, 800 })
        {
            editor.Width = width;
            Application.DoEvents();
            foreach (Button button in firstRow.Controls.Cast<Button>().Concat(mp3Row.Controls.Cast<Button>()).Append((Button)actions.GetControlFromPosition(0, 2)!))
            {
                Check(button.TextAlign == ContentAlignment.MiddleCenter && button.Padding == Padding.Empty, "centered action: " + button.Text);
                var text = TextRenderer.MeasureText(button.Text, button.Font);
                Check(text.Width <= button.ClientSize.Width && text.Height <= button.ClientSize.Height, "action text fits at " + width + ": " + button.Text);
                Check(button.Left >= 0 && button.Right <= button.Parent!.ClientSize.Width, "action stays inside row: " + button.Text);
            }
        }
        editor.Width = 620;
        Application.DoEvents();
        if (render)
        {
            editor.ShowInTaskbar = false;
            editor.StartPosition = FormStartPosition.Manual;
            editor.Location = new Point(-32000, -32000);
            editor.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(editor.Width, editor.Height);
            editor.DrawToBitmap(bitmap, new Rectangle(Point.Empty, editor.Size));
            bitmap.Save(Path.Combine(Environment.CurrentDirectory, ".test-temp", "editor-layout.png"));
        }
    }

    private static void TestLocalAudio(string directory)
    {
        using var form = NewForm(Path.Combine(directory, "local-audio"));
        var settings = Get(form, "settings");
        Check((string)settings.GetType().GetProperty("Mp3Quality")!.GetValue(settings)! == "0", "MP3 defaults to best quality");
        Check(!((CheckBox)Get(form, "downloadOnAdd")).Checked, "download is opt-in");
        var folder = Path.Combine(directory, "music");
        Directory.CreateDirectory(folder);
        Property(settings, "DownloadFolder", folder);
        Property(settings, "Mp3Quality", "192K");
        var playlistFolder = (string)Call(null, "PlaylistDownloadFolder", folder, "Мой плейлист")!;
        Check(playlistFolder == Path.Combine(folder, "Мой плейлист"), "download uses named playlist subfolder");
        var safeFolder = (string)Call(null, "PlaylistDownloadFolder", folder, "../CON:bad?")!;
        Check(Path.GetDirectoryName(safeFolder) == folder && !Path.GetFileName(safeFolder).Contains(':'), "playlist folder stays within chosen root");
        Check(Path.GetFileName((string)Call(null, "PlaylistDownloadFolder", folder, "CON")!) == "_CON", "reserved playlist folder escaped");
        var track = new Track { Id = "dQw4w9WgXcQ", Name = "test" };
        Check(Call(form, "LocalAudio", track) is null, "missing MP3 uses YouTube");
        var file = Path.Combine(folder, track.Id + ".mp3");
        File.WriteAllText(file, "fixture");
        Check((string?)Call(form, "LocalAudio", track) == file, "find MP3 by video ID");
        var resolved = ((Task<Uri>)Call(form, "ResolveAudioAsync", track, CancellationToken.None)!).GetAwaiter().GetResult();
        Check(resolved.IsFile && resolved.LocalPath == file, "local source resolves without yt-dlp");
        Call(form, "LoadLibrary");
        var duplicate = new Track { Id = track.Id };
        ((List<Playlist>)Get(form, "playlists")).Add(new Playlist { Name = "duplicate", Tracks = [duplicate] });
        ((Task)Call(form, "DownloadTrackAsync", track, folder, CancellationToken.None)!).GetAwaiter().GetResult();
        Check(track.LocalFile == file && duplicate.LocalFile == file, "existing MP3 reused and shared across playlists");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { ((Task<Uri>)Call(form, "ResolveAudioAsync", track, cancelled.Token)!).GetAwaiter().GetResult(); Check(false, "cancelled source resolution"); }
            catch (OperationCanceledException) { Check(true, "cancelled source resolution"); }
        }
        track.LocalFile = file;
        Property(settings, "DownloadFolder", Path.Combine(directory, "other"));
        Check((string?)Call(form, "LocalAudio", track) == file, "folder change preserves old files");
        Check((bool)Call(form, "SaveSettings")!, "MP3 settings saved");
        var loaded = Call(form, "LoadSettings")!;
        Check((string)loaded.GetType().GetProperty("Mp3Quality")!.GetValue(loaded)! == "192K", "quality survives settings reload");
        var library = Path.Combine(directory, "local-library.json");
        PlaylistStore.Save(library, [new Playlist { Name = "local", Tracks = [track] }]);
        Check(PlaylistStore.Load(library, library)[0].Tracks[0].LocalFile == file, "MP3 path survives library reload");
        Property(settings, "DownloadFolder", folder);
        var namedFolder = (string)Call(null, "PlaylistDownloadFolder", folder, "duplicate")!;
        Directory.CreateDirectory(namedFolder);
        var namedFile = Path.Combine(namedFolder, track.Id + ".mp3");
        File.WriteAllText(namedFile, "fixture");
        File.Delete(file);
        Check((string?)Call(form, "LocalAudio", track) == namedFile, "find MP3 inside playlist folder");
        track.LocalFile = namedFile;
        duplicate.LocalFile = namedFile;
        Call(form, "DeleteLocalAudio", track);
        Check(track.LocalFile is null && duplicate.LocalFile is null, "delete MP3 clears shared file references");
        Check(!File.Exists(namedFile), "delete MP3 removes local file");
        Check(((List<Playlist>)Get(form, "playlists"))[0].Tracks.Contains(duplicate), "delete MP3 preserves playlist track");
        Check(Call(form, "LocalAudio", track) is null, "deleted local file falls back to YouTube");
        track.Id = "../invalid";
        Check(Call(form, "LocalAudio", track) is null, "invalid ID cannot escape music folder");
    }

    private static void TestUrlsAndTime()
    {
        foreach (var url in new[] { "https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL123", "https://youtu.be/dQw4w9WgXcQ", "https://m.youtube.com/shorts/dQw4w9WgXcQ", "https://youtube.com/live/dQw4w9WgXcQ" })
        {
            object?[] args = [url, false];
            Check((bool)Call(null, "IsYouTubeUrl", args)! && !(bool)args[1]!, "video: " + url);
        }
        object?[] playlist = ["https://youtube.com/playlist?list=PL123", false];
        Check((bool)Call(null, "IsYouTubeUrl", playlist)! && (bool)playlist[1]!, "playlist URL");
        playlist = ["https://youtube.com/watch?list=PL123", false];
        Check((bool)Call(null, "IsYouTubeUrl", playlist)! && (bool)playlist[1]!, "watch playlist without video");
        foreach (var url in new[] { "https://youtube.evil.invalid/playlist?list=PL123", "ftp://youtube.com/watch?v=dQw4w9WgXcQ", "https://youtube.com/", "https://youtube.com/watch?v=bad", "https://youtube.com/playlist?list=", "https://user@youtube.com/watch?v=dQw4w9WgXcQ" })
            Check(!(bool)Call(null, "IsYouTubeUrl", url, false)!, "reject: " + url);
        Check((string)Call(null, "CleanVideoUrl", "https://youtube.com/watch?v=dQw4w9WgXcQ&list=PL123")! == "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "strip playlist from video");
        Check((string)Call(null, "FormatTime", 3660000L)! == "1:01:00", "hours");
        Check((string)Call(null, "FormatTime", 90061000L)! == "25:01:01", "more than one day");
        Check((string)Call(null, "FormatTime", 61000L)! == "1:01", "minutes");
    }

    private static void TestReleaseAssets()
    {
        using var release = JsonDocument.Parse("""{"assets":[{"name":"CatLuYNet-1.1.3-win-x64.zip","browser_download_url":"https://github.com/Maksimasz/CatLuYNet/releases/download/v1.1.3/app.zip"}]}""");
        Check(Call(null, "FindUpdateAsset", release.RootElement) is null, "updater requires an installer for automatic installation");

        using var withInstaller = JsonDocument.Parse("""{"assets":[{"name":"CatLuYNet-1.1.3-win-x64.zip","browser_download_url":"https://github.com/Maksimasz/CatLuYNet/releases/download/v1.1.3/app.zip"},{"name":"CatLuYNet-1.1.3-Setup.exe","browser_download_url":"https://github.com/Maksimasz/CatLuYNet/releases/download/v1.1.3/setup.exe"}]}""");
        Check((string?)Call(null, "FindUpdateAsset", withInstaller.RootElement) == "https://github.com/Maksimasz/CatLuYNet/releases/download/v1.1.3/setup.exe", "updater selects GitHub installer");

        using var empty = JsonDocument.Parse("""{"assets":[]}""");
        Check(Call(null, "FindUpdateAsset", empty.RootElement) is null, "update checker handles releases without assets");

        using var malformed = JsonDocument.Parse("""{"assets":[null,3,{"name":4,"browser_download_url":"https://github.com/Maksimasz/CatLuYNet/releases/download/bad.exe"},{"name":"CatLuYNet-1.1.4-Setup.exe","browser_download_url":false},{"name":"CatLuYNet-1.1.4-Setup.exe","browser_download_url":"https://example.invalid/setup.exe"},{"name":"CatLuYNet-1.1.4-Setup.exe","browser_download_url":"https://github.com/Maksimasz/CatLuYNet/releases/download/v1.1.4/setup.exe"}]}""");
        Check((string?)Call(null, "FindUpdateAsset", malformed.RootElement) == "https://github.com/Maksimasz/CatLuYNet/releases/download/v1.1.4/setup.exe", "update checker skips malformed and untrusted assets");
        Check((bool)Call(null, "IsTrustedUpdateUrl", new Uri("https://release-assets.githubusercontent.com/setup.exe"))!, "download allows official GitHub release assets");
        foreach (var url in new[] { "http://github.com/setup.exe", "https://example.invalid/setup.exe", "https://release-assets.githubusercontent.com.evil.invalid/setup.exe", "https://github.com:444/setup.exe" })
            Check(!(bool)Call(null, "IsTrustedUpdateUrl", new Uri(url))!, "reject update redirect: " + url);
        using var form = NewForm(Path.Combine(Path.GetTempPath(), "ynet-update-test-" + Guid.NewGuid().ToString("N")));
        Check(((System.Windows.Forms.Timer)Get(form, "updateCheckTimer")).Interval == (int)TimeSpan.FromHours(24).TotalMilliseconds, "update checks repeat every 24 hours");
        var updateLink = (LinkLabel)Get(form, "updateLink");
        Check(!updateLink.Visible, "update link stays hidden when no new release is found");
        Call(form, "SetUpdateLinkText", "Доступно обновление 1.2.0");
        Check(updateLink.LinkArea.Start == 0 && updateLink.LinkArea.Length == updateLink.Text.Length, "entire available version label is clickable");
    }

    private static void TestSharedCore(string root)
    {
        Check(YouTubeUrl.IsValid("https://youtube.com/playlist?list=PL123", out var isPlaylist) && isPlaylist, "shared playlist parser");
        Check(YouTubeUrl.GetPlaylistId("https://youtube.com/playlist?list=PL123") == "PL123", "shared playlist id");
        var path = Path.Combine(root, "shared", "stations.json");
        var linked = new Playlist { Name = "Shared", SourceUrl = "https://youtube.com/playlist?list=PL123", LastCheckedUtc = DateTime.UtcNow, Tracks = [new Track { Id = "dQw4w9WgXcQ", Name = "Track", Artist = "Artist", IsNew = true }] };
        PlaylistStore.Save(path, [linked]);
        var loaded = PlaylistStore.Load(path, Path.Combine(root, "missing.json"))[0];
        Check(loaded.Tracks[0].Id == "dQw4w9WgXcQ" && loaded.SourceUrl == linked.SourceUrl && loaded.Tracks[0].IsNew, "linked playlist metadata round trip");
        var next = new Track { Id = "abcdefghijk", Name = "New song", Artist = "Artist" };
        Check(PlaylistStore.PrependMissingTracks(loaded, [next, loaded.Tracks[0]]) == 1 && ReferenceEquals(loaded.Tracks[0], next) && next.IsNew, "new online track is marked and prepended");
        Check(PlaylistStore.PrependMissingTracks(loaded, [next]) == 0 && loaded.Tracks.Count == 2, "playlist refresh does not duplicate tracks");
        PlaylistStore.Save(path, [new Playlist { Name = "Invalid source", SourceUrl = "https://example.invalid/playlist", Tracks = [] }]);
        try { PlaylistStore.Load(path, Path.Combine(root, "missing.json")); throw new Exception("Invalid remote playlist source was accepted"); }
        catch (JsonException) { Check(true, "linked playlist source URL is validated"); }
    }

    private static void TestPlaylistTransfer(string root)
    {
        var path = Path.Combine(root, "transfer", "stations.json");
        var exportPath = Path.Combine(root, "export.json");
        var playlist = new Playlist { Name = "Музыка עברית", Expanded = false, SourceUrl = "https://youtube.com/playlist?list=PL123", LastCheckedUtc = DateTime.UtcNow,
            Tracks = [new Track { Id = "abcdefghijk", Name = "Первый", Artist = "Artist", IsNew = true }, new Track { Id = "dQw4w9WgXcQ", Name = "Второй" }] };
        PlaylistStore.Save(exportPath, [playlist, new Playlist { Name = "Пустой" }]);
        List<Playlist> existing = [new Playlist { Name = playlist.Name }];
        PlaylistStore.Save(path, existing);
        var original = File.ReadAllText(path);
        var imported = PlaylistStore.Import(exportPath, path, existing);
        Check(existing.Count == 1, "import leaves live collection untouched until successful commit");
        existing.AddRange(imported);
        var restored = PlaylistStore.Load(path, path);
        Check(restored.Count == 3 && restored[0].Tracks.Count == 0 && restored[2].Tracks.Count == 0, "import appends same-name and empty playlists");
        Check(JsonSerializer.Serialize(restored[1]) == JsonSerializer.Serialize(playlist), "export/import preserves all metadata, Unicode and track order");
        Check(File.ReadAllText(path + ".bak") == original, "import backs up previous library");
        var saved = File.ReadAllText(path);
        foreach (var invalid in new[] { "{", "{}", "null", "{\"Playlists\":null}", "{\"Playlists\":[null]}", "{\"Playlists\":[{\"Tracks\":[{\"Id\":\"bad\"}]}]}" })
        {
            File.WriteAllText(exportPath, invalid);
            try { PlaylistStore.Import(exportPath, path, existing); throw new Exception("Invalid import accepted"); }
            catch (JsonException) { Check(File.ReadAllText(path) == saved && existing.Count == 3, "invalid import preserves library"); }
        }
        PlaylistStore.Save(exportPath, [playlist]);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { PlaylistStore.Import(exportPath, path, existing); throw new Exception("Locked import accepted"); }
            catch (IOException) { Check(existing.Count == 3, "failed import does not change live collection"); }
        }
        Check(File.ReadAllText(path) == saved, "failed import preserves saved library");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0, "failed import cleans temporary files");
        PlaylistStore.Save(exportPath, []);
        Check(PlaylistStore.Import(exportPath, path, existing).Count == 0 && File.ReadAllText(path) == saved, "empty import is a no-op");
        File.Delete(exportPath);
        try { PlaylistStore.Import(exportPath, path, existing); throw new Exception("Missing import accepted"); }
        catch (FileNotFoundException) { Check(File.ReadAllText(path) == saved, "missing import preserves library"); }
    }

    private static void TestPlaylistExportSelection(string root)
    {
        Check((string)Call(null, "PlaylistFileName", "Моя музыка")! == "Моя музыка.json", "export filename uses playlist name");
        Check((string)Call(null, "PlaylistFileName", "Rock/Pop: Hits?")! == "Rock_Pop_ Hits_.json", "export filename replaces forbidden characters");
        Check((string)Call(null, "PlaylistFileName", " .. ")! == "Playlist.json", "empty filename has fallback");
        Check((string)Call(null, "PlaylistFileName", "con")! == "_con.json", "reserved Windows filename is escaped");
        using var form = NewForm(Path.Combine(root, "export-selection"));
        Call(form, "LoadLibrary");
        var playlists = (List<Playlist>)Get(form, "playlists");
        playlists.AddRange([new Playlist { Name = "Same name" }, new Playlist { Name = "Same name", Tracks = [new Track { Id = "abcdefghijk", Name = "Selected track" }] }]);
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var cancel = false;
        var emptyDisabled = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var dialog = Application.OpenForms.Cast<Form>().Last();
            var list = dialog.Controls.OfType<ListBox>().Single();
            var button = dialog.Controls.OfType<Button>().Single();
            emptyDisabled = list.Items.Count == 0 && !button.Enabled;
            if (cancel || list.Items.Count == 0) dialog.DialogResult = DialogResult.Cancel;
            else { list.SelectedIndex = 1; button.PerformClick(); }
        };
        timer.Start();
        var selected = (Playlist)Call(form, "SelectPlaylistForExport", form)!;
        Check(ReferenceEquals(selected, playlists[1]), "export selects exact playlist even with identical names");
        var path = Path.Combine(root, "selected-export.json");
        PlaylistStore.Save(path, [selected]);
        var exported = PlaylistStore.Load(path, path);
        Check(exported.Count == 1 && exported[0].Tracks.Single().Id == "abcdefghijk", "individual export contains only selected playlist");
        var target = Path.Combine(root, "individual-import.json");
        var imported = PlaylistStore.Import(path, target, [playlists[0]]);
        var restored = PlaylistStore.Load(target, target);
        Check(imported.Count == 1 && restored.Count == 2 && restored[0].Tracks.Count == 0 && restored[1].Tracks.Count == 1, "individual import keeps same-name playlists separate");
        cancel = true;
        timer.Start();
        Check(Call(form, "SelectPlaylistForExport", form) is null && playlists.Count == 2, "cancel export selection leaves playlists unchanged");
        playlists.Clear();
        timer.Start();
        Check(Call(form, "SelectPlaylistForExport", form) is null && emptyDisabled, "empty library cannot confirm export");
    }

    private static void TestStorage(string root)
    {
        var directory = Path.Combine(root, "storage");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "stations.json");
        foreach (var bad in new[] { "{\"Playlists\":[", "{\"Playlists\":[{\"Tracks\":null}]}" })
        {
            File.WriteAllText(path, bad);
            using var form = NewForm(directory);
            Call(form, "LoadLibrary");
            Check(!(bool)Call(form, "SaveLibrary")!, "reject save after invalid load");
            Check(File.ReadAllText(path) == bad, "preserve invalid original");
        }
        File.Delete(path);
        using (var form = NewForm(directory))
        {
            Call(form, "LoadLibrary");
            Check((bool)Call(form, "SaveLibrary")!, "save new library");
            var original = File.ReadAllText(path);
            Check((bool)Call(form, "SaveLibrary")!, "atomic replacement");
            Check(File.ReadAllText(path + ".bak") == original, "backup last library");
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Check(!(bool)Call(form, "SaveLibrary")!, "locked file handled");
            Check(File.ReadAllText(path) == original, "failed write preserves original");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "temporary file cleaned");
        }
        File.WriteAllText(path, "[{\"Id\":\"dQw4w9WgXcQ\",\"Name\":\"Legacy\",\"Artist\":\"Artist\"}]");
        using (var form = NewForm(directory))
        {
            Call(form, "LoadLibrary");
            Check(((IList)Get(form, "playlists")).Count == 1, "old array format loads");
            Check((bool)Call(form, "SaveLibrary")!, "old array format migrates");
        }
    }

    private static void TestPlaybackAndControls(string root)
    {
        var emptyDirectory = Path.Combine(root, "empty-playlist-delete");
        using (var emptyForm = NewForm(emptyDirectory))
        {
            Call(emptyForm, "LoadLibrary");
            var empty = Model("Playlist"); Property(empty, "Name", "Empty");
            ((IList)Get(emptyForm, "playlists")).Add(empty);
            Call(emptyForm, "DeletePlaylist", empty);
            Check(((IList)Get(emptyForm, "playlists")).Count == 0 &&
                JsonDocument.Parse(File.ReadAllText(Path.Combine(emptyDirectory, "stations.json"))).RootElement.GetProperty("Playlists").GetArrayLength() == 0,
                "empty playlist can be deleted and saved");
        }

        using var form = NewForm(Path.Combine(root, "playback"));
        Call(form, "LoadLibrary");
        var playlist = Model("Playlist");
        var tracks = (IList)playlist.GetType().GetProperty("Tracks")!.GetValue(playlist)!;
        var track = Model("Track"); Property(track, "Id", "dQw4w9WgXcQ"); Property(track, "Name", "First");
        tracks.Add(track);
        ((IList)Get(form, "playlists")).Add(playlist);
        Set(form, "currentPlaylist", playlist); Set(form, "currentTrack", track);
        var settings = Get(form, "settings");
        Property(settings, "Theme", 2);
        Call(form, "ApplySettings");
        Check(((Label)Get(form, "status")).BackColor == Color.White, "status bar follows light theme surface");
        Check(ReferenceEquals(Call(form, "GetNextTrack"), track), "single-track wrap");
        var request = ((CancellationTokenSource)Get(form, "playbackRequest")).Token;
        Call(form, "RemoveTrack", playlist, track);
        Check(request.IsCancellationRequested, "deletion cancels pending playback");
        Check(Call(form, "GetNextTrack") is null, "empty queue returns no track");
        ((Task)Call(form, "NextAsync")!).GetAwaiter().GetResult();
        ((Task)Call(form, "PreviousAsync")!).GetAwaiter().GetResult();
        Check(Get(form, "currentTrack") is null, "deleted current track cleared");

        tracks.Add(track);
        var second = Model("Track"); Property(second, "Id", "abcdefghijk"); Property(second, "Name", "Second"); tracks.Add(second);
        Set(form, "currentTrack", track);
        var unrelated = Model("Track"); tracks.Add(unrelated);
        request = ((CancellationTokenSource)Get(form, "playbackRequest")).Token;
        Call(form, "RemoveTrack", playlist, unrelated);
        Check(!request.IsCancellationRequested, "unrelated deletion keeps playback");
        using var list = new ListBox { BindingContext = new BindingContext(), DataSource = tracks };
        list.SelectedItem = second;
        Action refresh = () => { list.DataSource = null; list.DataSource = tracks; };
        Call(form, "MoveTrack", playlist, list, -1, refresh);
        using var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "playback", "stations.json")));
        Check(saved.RootElement.GetProperty("Playlists")[0].GetProperty("Tracks")[0].GetProperty("Id").GetString() == "abcdefghijk", "reorder saved immediately");
        var host = (FlowLayoutPanel)Get(form, "playlistHost");
        var oldCard = host.Controls[0];
        Call(form, "RenderPlaylists", false);
        Check(oldCard.IsDisposed, "old card disposed");

        _ = form.Handle;
        var player = (MediaPlayer)Get(form, "player");
        Check(!player.Mute, "audio player starts unmuted");
        // LibVLC reports -1 until media opens an audio output, so test the UI/player mute state before playback.
        Call(form, "SetPlaybackVolume", 37);
        Check(!player.Mute && ((TrackBar)Get(form, "volume")).Value == 37, "volume control updates the visible level and unmutes the player");
        var invoked = false;
        Call(form, "PostPlayerEvent", player, (Action)(() => invoked = true));
        request = ((CancellationTokenSource)Get(form, "playbackRequest")).Token;
        Call(form, "Stop");
        Application.DoEvents();
        Check(request.IsCancellationRequested && !invoked, "stop invalidates queued playback callback");

        Set(form, "isCrossfading", true);
        Call(form, "PostPlayerEnded", player);
        Application.DoEvents();
        Check((bool)Get(form, "currentTrackEnded"), "outgoing track end is retained while crossfade stream loads");
        Call(form, "Stop");

        var media = new Media((LibVLC)Get(form, "vlc"), new Uri(Path.Combine(root, "unused.wav")));
        Set(form, "media", media);
        var incoming = new MediaPlayer((LibVLC)Get(form, "vlc"));
        Set(form, "incomingPlayer", incoming);
        Set(form, "isCrossfading", true);
        var elapsed = (Stopwatch)Get(form, "crossfadeElapsed"); elapsed.Start();
        Call(form, "PausePlayback");
        Check((bool)Get(form, "paused") && !elapsed.IsRunning, "pause freezes fade clock");
        ((Task)Call(form, "PlayCurrentAsync")!).GetAwaiter().GetResult();
        Check(!(bool)Get(form, "paused") && elapsed.IsRunning && ReferenceEquals(Get(form, "media"), media), "resume retains media and resumes fade clock");
        Call(form, "Stop");
    }

    private static ProcessStartInfo Child(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
    private static async Task TestProcessAsync(string directory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await (Task<(string Output, string Error, int ExitCode)>)Call(null, "ReadProcessAsync", Child("pipes"), timeout.Token)!;
        Check(result.Output.Length == 1_000_000 && result.Error.Length == 1_000_000 && result.ExitCode == 0, "drain both pipes without deadlock");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pidPath = Path.Combine(directory, "child.pid");
        var pending = (Task<(string, string, int)>)Call(null, "ReadProcessAsync", Child("wait", pidPath), cancel.Token)!;
        while (!File.Exists(pidPath)) await Task.Delay(20, cancel.Token);
        var pid = int.Parse(await File.ReadAllTextAsync(pidPath));
        cancel.Cancel();
        try { await pending; throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { checks++; }
        try { using var child = Process.GetProcessById(pid); Check(child.HasExited, "child terminated"); }
        catch (ArgumentException) { checks++; }
    }
}
