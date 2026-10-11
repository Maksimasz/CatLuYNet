using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LibVLCSharp.Shared;
using YouTubeRadio;

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
            TestStorage(directory);
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
    private static object Model(string name) => Activator.CreateInstance(FormType.GetNestedType(name, BindingFlags.NonPublic)!)!;
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);

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
        using var form = NewForm(Path.Combine(root, "playback"));
        Call(form, "LoadLibrary");
        var playlist = Model("Playlist");
        var tracks = (IList)playlist.GetType().GetProperty("Tracks")!.GetValue(playlist)!;
        var track = Model("Track"); Property(track, "Id", "dQw4w9WgXcQ"); Property(track, "Name", "First");
        tracks.Add(track);
        ((IList)Get(form, "playlists")).Add(playlist);
        Set(form, "currentPlaylist", playlist); Set(form, "currentTrack", track);
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
        var invoked = false;
        Call(form, "PostPlayerEvent", player, (Action)(() => invoked = true));
        request = ((CancellationTokenSource)Get(form, "playbackRequest")).Token;
        Call(form, "Stop");
        Application.DoEvents();
        Check(request.IsCancellationRequested && !invoked, "stop invalidates queued playback callback");

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
