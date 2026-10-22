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
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru");
        var directory = Path.Combine(Path.GetTempPath(), "ynet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (args.Contains("--localization-only"))
            {
                TestLocalization(directory);
                Console.WriteLine($"PASS: {checks} localization checks");
                return 0;
            }
            TestUrlsAndTime();
            TestReleaseAssets();
            Check(((string)Call(null, "FormatYtDlpError", "WARNING: initial data\nWARNING: runtime\nERROR: [youtube] id: Video unavailable")!).Contains("видео недоступно"), "download reports actual error after warnings");
            Check((string)Call(null, "FormatYtDlpError", "WARNING: warning\nERROR: conversion failed")! == "ERROR: conversion failed", "conversion error is preserved");
            Check(!string.IsNullOrWhiteSpace((string)Call(null, "FormatYtDlpError", "")!), "empty downloader error has explanation");
            TestWindowsMp3Tags(directory);
            TestLocalAudio(directory);
            TestEditorButtons(directory, args.Contains("--render-editor"));
            TestArtwork(directory);
            TestSharedCore(directory);
            TestTrackDuplicates(directory);
            TestStorage(directory);
            TestPlaylistTransfer(directory);
            TestPlaylistExportSelection(directory);
            TestPlaybackAndControls(directory);
            TestLocalization(directory);
            // No WinForms continuation is needed by the subprocess checks.
            SynchronizationContext.SetSynchronizationContext(null);
            TestProcessAsync(directory).GetAwaiter().GetResult();
            Console.WriteLine($"PASS: {checks} checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void TestLocalization(string directory)
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
        var translations = (Dictionary<string, Dictionary<string, string>>)FormType
            .GetField("Translations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var keys = translations["ru"].Keys.ToHashSet();
        foreach (var (language, texts) in translations)
        {
            Check(keys.SetEquals(texts.Keys), "complete translation catalog: " + language);
            foreach (var (key, text) in texts)
            {
                Check(!string.IsNullOrWhiteSpace(text), "nonempty translation: " + language + "/" + key);
                var format = System.Text.CompositeFormat.Parse(text);
                Check(format.MinimumArgumentCount == System.Text.CompositeFormat.Parse(translations["ru"][key]).MinimumArgumentCount,
                    "matching format arguments: " + language + "/" + key);
                Check(language == "ru" || !text.Any(c => c is >= '\u0400' and <= '\u04ff'),
                    "no Russian fallback: " + language + "/" + key);
            }
        }

        static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
            .SelectMany(child => new[] { child }.Concat(Descendants(child)));
        try
        {
            using var form = NewForm(Path.Combine(directory, "translations"));
            var settings = Get(form, "settings");
            for (var index = 1; index <= 4; index++)
            {
                var code = new[] { "ru", "en", "he", "lt" }[index - 1];
                var texts = translations[code];
                Property(settings, "Language", index);
                Call(form, "ApplySettings");
                Check(((CheckBox)Get(form, "downloadOnAdd")).Text == texts["downloadMp3"], "main checkbox switches: " + code);
                Check(((Label)Get(form, "status")).Text == texts["ready"], "existing status switches: " + code);
                Check((string)Call(form, "T", "checkUpdates")! == texts["checkUpdates"], "update action translated: " + code);
                Check(((string)Call(null, "FormatYtDlpError", "ERROR: Video unavailable")!) == texts["videoUnavailable"],
                    "downloader errors follow language: " + code);
                Check((string)Call(null, "TextForLanguage", "fr", "save")! == translations["en"]["save"], "unsupported language uses English");
                var type = FormType.GetNestedType("PlaylistEditorDialog", BindingFlags.NonPublic)!;
                var playlist = new Playlist { Name = "User title עברית", Tracks = [new Track { Id = "dQw4w9WgXcQ", Name = "User song" }] };
                using var editor = (Form)Activator.CreateInstance(type, form, playlist)!;
                Check(editor.Text == string.Format(texts["playlist"], playlist.Name), "editor title preserves user data: " + code);
                Check(editor.RightToLeft == (code == "he" ? RightToLeft.Yes : RightToLeft.No), "editor text direction: " + code);
                Check(Descendants(editor).OfType<Button>().Any(button => button.Text == texts["downloadSelectedMp3"]),
                    "download action translated: " + code);
                Check(code == "ru" || Descendants(editor).All(control => !control.Text.Any(c => c is >= '\u0400' and <= '\u04ff')),
                    "editor has no Russian UI: " + code);
                Check(playlist.Name == "User title עברית" && playlist.Tracks[0].Name == "User song", "language does not change library data: " + code);
                editor.ShowInTaskbar = false;
                editor.StartPosition = FormStartPosition.Manual;
                editor.Location = new Point(-32000, -32000);
                editor.Show();
                foreach (var width in new[] { 500, 620 })
                {
                    editor.Width = width;
                    Application.DoEvents();
                    foreach (var button in Descendants(editor).OfType<Button>())
                    {
                        var measured = TextRenderer.MeasureText(button.Text, button.Font);
                        Check(measured.Width <= button.ClientSize.Width && measured.Height <= button.ClientSize.Height,
                            "translated editor button fits: " + code + "/" + width + "/" + button.Text);
                    }
                }
                if (Environment.GetCommandLineArgs().Contains("--render-localization"))
                {
                    var output = Path.Combine(Environment.CurrentDirectory, ".test-temp", "localization");
                    Directory.CreateDirectory(output);
                    using var image = new Bitmap(editor.Width, editor.Height);
                    editor.DrawToBitmap(image, new Rectangle(Point.Empty, editor.Size));
                    image.Save(Path.Combine(output, "editor-" + code + ".png"));
                }
                editor.Hide();
                Exception? dialogError = null;
                using var timer = new System.Windows.Forms.Timer { Interval = 50 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    var dialog = Application.OpenForms.Cast<Form>().Last();
                    try
                    {
                        Check(dialog.Text == texts["settings"], "settings title: " + code);
                        Check(Descendants(dialog).Any(control => control.Text == texts["mp3Quality"]), "MP3 quality translated: " + code);
                        Check(Descendants(dialog).OfType<ComboBox>().Any(combo => combo.Items.Contains(texts["bitrate128"])), "bitrate choices translated: " + code);
                        Check(dialog.RightToLeft == (code == "he" ? RightToLeft.Yes : RightToLeft.No), "settings text direction: " + code);
                        if (Environment.GetCommandLineArgs().Contains("--render-localization"))
                        {
                            var output = Path.Combine(Environment.CurrentDirectory, ".test-temp", "localization");
                            Directory.CreateDirectory(output);
                            using var image = new Bitmap(dialog.Width, dialog.Height);
                            dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                            image.Save(Path.Combine(output, "settings-" + code + ".png"));
                        }
                    }
                    catch (Exception ex) { dialogError = ex; }
                    finally { dialog.Close(); }
                };
                timer.Start();
                Call(form, "ShowSettings");
                if (dialogError is not null) throw dialogError;
            }
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = originalCulture; }
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

    private static void TestArtwork(string directory)
    {
        var covered = Path.Combine(directory, "covered.mp3");
        var bare = Path.Combine(directory, "bare.mp3");
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        void Generate(params string[] args)
        {
            var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = ((Task<(string Output, string Error, int ExitCode)>)Call(null, "ReadProcessAsync", start, timeout.Token)!).GetAwaiter().GetResult();
            Check(result.ExitCode == 0, "artwork fixture generated: " + result.Error);
        }
        void Pump(Task task)
        {
            var wait = Stopwatch.StartNew();
            while (!task.IsCompleted && wait.Elapsed < TimeSpan.FromSeconds(20)) { Application.DoEvents(); Thread.Sleep(10); }
            Check(task.IsCompleted, "artwork request finishes");
            task.GetAwaiter().GetResult();
        }
        Generate("-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=duration=0.2", "-f", "lavfi", "-i", "color=blue:s=64x32", "-map", "0:a", "-map", "1:v", "-c:a", "libmp3lame", "-c:v", "mjpeg", "-frames:v", "1", "-disposition:v", "attached_pic", "-id3v2_version", "3", covered);
        Generate("-hide_banner", "-loglevel", "error", "-i", covered, "-map", "0:a", "-c:a", "copy", bare);
        var read = (Task<Image?>)Call(null, "ReadTrackArtworkAsync", covered, CancellationToken.None)!;
        Pump(read);
        using var image = read.Result;
        Check(image is not null && image.Width == image.Height * 2, "embedded cover loaded with original aspect ratio");
        var missing = (Task<Image?>)Call(null, "ReadTrackArtworkAsync", bare, CancellationToken.None)!;
        Pump(missing);
        Check(missing.Result is null, "MP3 without cover keeps cat fallback");
        using var form = NewForm(Path.Combine(directory, "cover-form"));
        var picture = (PictureBox)Get(form, "trackArtwork");
        Check(picture.SizeMode == PictureBoxSizeMode.Zoom, "cover uses proportional zoom");
        var stale = (Task)Call(form, "LoadTrackArtworkAsync", covered, (int)Get(form, "artworkVersion") - 1, CancellationToken.None)!;
        Pump(stale);
        Check(picture.Image is null, "obsolete track artwork ignored");
        var current = (Task)Call(form, "LoadTrackArtworkAsync", covered, (int)Get(form, "artworkVersion"), CancellationToken.None)!;
        Pump(current);
        Check(picture.Image is not null && !((MediaPlayer)Get(form, "catPlayer")).IsPlaying, "cover replaces cat video");
        Call(form, "ClearTrackArtwork");
        Check(picture.Image is null && !picture.Visible, "cover cleared on reset");
        File.Delete(covered);
        Check(!File.Exists(covered), "cover display does not lock MP3 file");
    }

    private static void TestWindowsMp3Tags(string directory)
    {
        var file = Path.Combine(directory, "windows-tags.mp3");
        byte[] original = [73, 68, 51, 3, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 255, 251, 144, 100];
        File.WriteAllBytes(file, original);
        const string url = "https://www.youtube.com/watch?v=abcdefghijk";
        Call(null, "AddWindowsMp3Tags", file, url);
        var result = File.ReadAllBytes(file);
        Check(System.Text.Encoding.ASCII.GetString(result, 10, 4) == "WOAR", "MP3 has native author URL frame");
        var urlSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(result.AsSpan(14, 4));
        Check(System.Text.Encoding.ASCII.GetString(result, 20, urlSize) == url, "author URL points to YouTube video");
        var ratingOffset = 20 + urlSize;
        Check(System.Text.Encoding.ASCII.GetString(result, ratingOffset, 4) == "POPM", "MP3 has native rating frame");
        var ratingPayload = ratingOffset + 10;
        var emailEnd = Array.IndexOf(result, (byte)0, ratingPayload);
        Check(result[emailEnd + 1] == 255, "MP3 Windows rating is five stars");
        Check(result.AsSpan(result.Length - original.Length + 10).SequenceEqual(original.AsSpan(10)), "original metadata and audio bytes preserved");
        var size = (result[6] << 21) | (result[7] << 14) | (result[8] << 7) | result[9];
        Check(size == result.Length - 14, "ID3 size accounts for added frames");
        Check(!System.Text.Encoding.ASCII.GetString(result).Contains("OWNR"), "no ownership tag added");
        original[3] = 4;
        File.WriteAllBytes(file, original);
        try { Call(null, "AddWindowsMp3Tags", file, url); Check(false, "reject unsupported ID3 layout"); }
        catch (InvalidDataException) { Check(File.ReadAllBytes(file).SequenceEqual(original), "unsupported MP3 remains unchanged"); }
        if (File.Exists(Path.Combine(Environment.CurrentDirectory, ".test-temp", "mp3-tags", "tagged.mp3")))
        {
            var real = Path.Combine(directory, "real-tags.mp3");
            File.Copy(Path.Combine(Environment.CurrentDirectory, ".test-temp", "mp3-tags", "tagged.mp3"), real);
            Call(null, "AddWindowsMp3Tags", real, url);
            File.Copy(real, Path.Combine(Environment.CurrentDirectory, ".test-temp", "mp3-tags", "windows-tags.mp3"), true);
        }
    }

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
        Check((string)Call(null, "Mp3FileName", track)! == "test [dQw4w9WgXcQ].mp3", "MP3 filename includes track title");
        var titledTrack = new Track { Id = "abcdefghijk", Name = "Осень / סתיו: Песня?" };
        var titledName = (string)Call(null, "Mp3FileName", titledTrack)!;
        Check(titledName == "Осень _ סתיו_ Песня_ [abcdefghijk].mp3", "MP3 title keeps Unicode and escapes Windows characters");
        Check(titledName != (string)Call(null, "Mp3FileName", new Track { Id = track.Id, Name = titledTrack.Name })!, "same titles use distinct filenames");
        Check(((string)Call(null, "Mp3FileName", new Track { Id = track.Id, Name = new string('я', 400) })!).Length < 180, "long MP3 title is bounded");
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
        var titledPath = Path.Combine(folder, titledName);
        File.WriteAllText(titledPath, "fixture");
        ((Task)Call(form, "DownloadTrackAsync", titledTrack, folder, CancellationToken.None)!).GetAwaiter().GetResult();
        Check(titledTrack.LocalFile == titledPath, "download uses titled MP3 path without overwriting existing file");
        titledTrack.LocalFile = null;
        Property(settings, "DownloadFolder", folder);
        Check((string?)Call(form, "LocalAudio", titledTrack) == titledPath, "titled MP3 is found without stored path");
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

    private static void TestTrackDuplicates(string root)
    {
        using var form = NewForm(Path.Combine(root, "duplicates"));
        var first = new Playlist { Name = "First", Tracks = [new Track { Id = "dQw4w9WgXcQ", Name = "  Same title  " }] };
        var selected = new Playlist { Name = "Selected" };
        var library = (List<Playlist>)Get(form, "playlists");
        library.AddRange([first, selected]);
        Check(ReferenceEquals(Call(form, "FindTrackPlaylist", "dQw4w9WgXcQ"), first), "link duplicates are found in other playlists");
        Check(Call(form, "FindTrackPlaylist", "abcdefghijk") is null, "different video ID is allowed");
        Check(Call(form, "FindTrackPlaylist", "DQw4w9WgXcQ") is null, "YouTube IDs remain case sensitive");
        foreach (var url in new[] { "https://youtu.be/dQw4w9WgXcQ?t=10", "https://youtube.com/shorts/dQw4w9WgXcQ", "https://youtube.com/watch?v=dQw4w9WgXcQ&list=PL123" })
            Check(YouTubeUrl.CleanVideoUrl(url) == "https://www.youtube.com/watch?v=" + first.Tracks[0].Id, "URL variants resolve to same duplicate ID");
        Check((bool)Call(null, "HasSameTrackName", first, "same TITLE")!, "title matching ignores case and surrounding spaces");
        Check(!(bool)Call(null, "HasSameTrackName", selected, "Same title")!, "title matching is limited to selected playlist");
        Check(!(bool)Call(null, "HasSameTrackName", first, "Different title")!, "different title does not warn");
        Check(!(bool)Call(null, "HasSameTrackName", first, " ")!, "blank title does not warn");
        Check(first.Tracks.Count == 1 && selected.Tracks.Count == 0, "duplicate checks do not modify library");
        var remix = "In The Army Now (Club House Remix 2026) | Dance Pop & Eurodance Vibes";
        var official = "Captain Jack - In The Army Now 2017 [Official Video HD]";
        first.Tracks[0].Name = remix;
        Check((bool)Call(null, "HasSameTrackName", first, official)!, "reported remix and official video warn as same song");
        first.Tracks[0].Name = official;
        Check((bool)Call(null, "HasSameTrackName", first, remix)!, "song comparison works in reverse order");
        Check((bool)Call(null, "HasSameTrackName", first, "Captain Jack — IN  THE\u00a0ARMY NOW (Live)")!, "song comparison handles dash variants, whitespace and version labels");
        Check(!(bool)Call(null, "HasSameTrackName", first, "In The Army Tomorrow")!, "sharing some words is not enough to warn");
        Check(!(bool)Call(null, "HasSameTrackName", first, "(Official Video)")!, "empty normalized titles do not warn");
        foreach (var title in new[]
        {
            "Status Quo 'In The Army Now' (Live at Wacken 2017) - from 'Down Down & Dirty At Wacken",
            "uro & Status Quo - In The Army Now (Remix) 💥💃 🕺 ✈ dance mix"
        })
        {
            Check((bool)Call(null, "HasSameTrackName", first, title)!, "quoted live or emoji remix title matches song");
            first.Tracks[0].Name = title;
            Check((bool)Call(null, "HasSameTrackName", first, official)!, "quoted live or emoji remix title matches in reverse");
        }
        Check((string)Call(null, "NormalizeSongName", "Artist 'Don't Stop' (Live)")! == "Don t Stop", "apostrophe inside quoted song is preserved during extraction");
        Check((string)Call(null, "NormalizeSongName", "Live Forever")! == "Live Forever", "version word inside song name is preserved");
        first.Tracks[0].Name = "In The Army Now";
        Check((bool)Call(null, "HasSameTrackName", first, "Status Quo In The Army Now special performance")!, "complete song phrase is found between arbitrary surrounding words");
        Check(!(bool)Call(null, "HasSameTrackName", first, "In The Navy Now")!, "word sequence must remain consecutive");
        Check(!(bool)Call(null, "HasSameTrackName", first, "In The Army Nowadays")!, "word matching does not match part of another word");
        Check(!(bool)Call(null, "SongNamesMatch", "Love", "Love Story")!, "one shared word does not trigger a warning");
        Check((bool)Call(null, "SongNamesMatch", "Special performance In The Army Now", "in the army now")!, "word sequence matching works in both directions");
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
