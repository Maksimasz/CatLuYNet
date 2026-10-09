using System.Diagnostics;
using System.Text.Json;
using LibVLCSharp.Shared;

namespace YouTubeRadio;

public sealed class NativeMainForm : Form
{
    private readonly TextBox urlBox = new() { PlaceholderText = "Ссылка на ролик или плейлист YouTube", Dock = DockStyle.Fill };
    private readonly ListBox stations = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 62, BackColor = Color.FromArgb(31, 31, 31), BorderStyle = BorderStyle.None };
    private readonly TrackBar volume = new() { Minimum = 0, Maximum = 100, Value = 70, Width = 170, TickStyle = TickStyle.None };
    private readonly Panel equalizer = new() { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.FromArgb(20, 20, 20) };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(12, 3, 0, 0), ForeColor = Color.Silver, Text = "Готов" };
    private readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 120 };
    private readonly List<Station> items = [];
    private readonly string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatLuYNet", "stations.json");
    private readonly string legacyStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeRadio", "state.json");
    private readonly LibVLC vlc;
    private readonly MediaPlayer player;
    private Media? media;
    private bool playing;

    public NativeMainForm()
    {
        Core.Initialize();
        vlc = new LibVLC("--no-video", "--network-caching=1000");
        player = new MediaPlayer(vlc) { Volume = volume.Value };

        Text = "CatLu YNet";
        MinimumSize = new Size(540, 430);
        Size = new Size(720, 520);
        BackColor = Color.FromArgb(31, 31, 31);
        ForeColor = Color.White;

        var add = Button("Добавить");
        var play = Button("▶");
        var pause = Button("❚❚");
        var next = Button("▶▶");
        var remove = Button("Удалить");
        var header = new Label { Text = "📺  CatLu YNet", Dock = DockStyle.Top, Height = 46, Padding = new Padding(18, 13, 0, 0), Font = new Font(Font, FontStyle.Bold), ForeColor = Color.White };
        var tabs = new Label { Text = "     Станции              Поиск              Избранное              История              Эквалайзер       ⚙", Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 14, 0, 0), ForeColor = Color.FromArgb(190, 190, 190), Font = new Font(Font, FontStyle.Bold) };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 2, Padding = new Padding(10) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(urlBox, 0, 0);
        top.Controls.Add(add, 1, 0);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(10, 6, 10, 6) };
        controls.Controls.AddRange([play, pause, next, new Label { Text = "Громкость", AutoSize = true, Padding = new Padding(8, 9, 0, 0), ForeColor = Color.Silver }, volume, remove]);
        Controls.Add(stations);
        Controls.Add(equalizer);
        Controls.Add(status);
        Controls.Add(controls);
        Controls.Add(tabs);
        Controls.Add(top);
        Controls.Add(header);

        Load += (_, _) => LoadStations();
        FormClosing += (_, _) => { SaveStations(); Stop(); player.Dispose(); vlc.Dispose(); };
        add.Click += async (_, _) => await AddAsync();
        play.Click += async (_, _) => await PlaySelectedAsync();
        pause.Click += (_, _) => player.Pause();
        next.Click += async (_, _) => await NextAsync();
        remove.Click += (_, _) => RemoveSelected();
        volume.Scroll += (_, _) => player.Volume = volume.Value;
        stations.MouseClick += async (_, _) => await PlaySelectedAsync();
        stations.DrawItem += DrawStation;
        equalizer.Paint += DrawEqualizer;
        meterTimer.Tick += (_, _) => equalizer.Invalidate();
        player.Playing += (_, _) => BeginInvoke(() => { playing = true; meterTimer.Start(); status.Text = "Играет"; });
        player.Paused += (_, _) => BeginInvoke(() => { playing = false; meterTimer.Stop(); status.Text = "Пауза"; });
        player.EncounteredError += (_, _) => BeginInvoke(() => status.Text = "Ошибка аудиопотока" );
        player.EndReached += (_, _) => BeginInvoke(async () => await NextAsync());
    }

    private async Task AddAsync()
    {
        if (!IsYouTubeUrl(urlBox.Text, out var playlist)) { status.Text = "Введите ссылку YouTube"; return; }
        try
        {
            status.Text = "Чтение YouTube...";
            var received = playlist ? await ReadPlaylistAsync(urlBox.Text) : [await ReadVideoAsync(urlBox.Text)];
            foreach (var item in received.Where(item => items.All(existing => existing.Id != item.Id))) items.Add(item);
            RefreshStations();
            SaveStations();
            urlBox.Clear();
            status.Text = "Готов";
        }
        catch (Exception ex) { status.Text = $"Ошибка: {Short(ex.Message)}"; }
    }

    private async Task PlaySelectedAsync()
    {
        if (stations.SelectedItem is not Station station) return;
        try
        {
            status.Text = "Получение аудиопотока...";
            var result = await RunYtDlpAsync("--no-playlist", "-f", "bestaudio/best", "-g", $"https://www.youtube.com/watch?v={station.Id}");
            var stream = result.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(stream)) throw new InvalidOperationException("yt-dlp не вернул поток.");
            Stop();
            media = new Media(vlc, new Uri(stream));
            player.Volume = volume.Value;
            if (!player.Play(media)) throw new InvalidOperationException("LibVLC не открыл поток.");
        }
        catch (Exception ex) { status.Text = $"Ошибка: {Short(ex.Message)}"; }
    }

    private async Task NextAsync()
    {
        if (items.Count == 0) return;
        stations.SelectedIndex = (stations.SelectedIndex + 1 + items.Count) % items.Count;
        await PlaySelectedAsync();
    }

    private void RemoveSelected()
    {
        if (stations.SelectedItem is not Station station) return;
        items.Remove(station);
        RefreshStations();
        SaveStations();
    }

    private void Stop()
    {
        player.Stop();
        media?.Dispose();
        media = null;
    }

    private async Task<Station> ReadVideoAsync(string url)
    {
        using var document = JsonDocument.Parse(await RunYtDlpAsync("--no-playlist", "--dump-single-json", url));
        return ToStation(document.RootElement);
    }

    private async Task<List<Station>> ReadPlaylistAsync(string url)
    {
        var output = await RunYtDlpAsync("--flat-playlist", "--dump-json", url);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return ToStation(document.RootElement);
        }).Where(item => !string.IsNullOrWhiteSpace(item.Id)).ToList();
    }

    private static Station ToStation(JsonElement value) => new()
    {
        Id = value.GetProperty("id").GetString() ?? "",
        Name = value.GetProperty("title").GetString() ?? "Без названия",
        Artist = value.TryGetProperty("uploader", out var author) ? author.GetString() ?? "YouTube" : "YouTube"
    };

    private static async Task<string> RunYtDlpAsync(params string[] arguments)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "tools", "yt-dlp.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("Не найден tools\\yt-dlp.exe.");
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("yt-dlp не запустился.");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return output;
    }

    private static bool IsYouTubeUrl(string text, out bool playlist)
    {
        playlist = false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (!uri.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))) return false;
        playlist = uri.Query.Contains("list=", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void LoadStations()
    {
        try
        {
            var path = File.Exists(statePath) ? statePath : legacyStatePath;
            if (File.Exists(path)) items.AddRange(JsonSerializer.Deserialize<List<Station>>(File.ReadAllText(path)) ?? []);
        }
        catch (JsonException) { }
        RefreshStations();
    }

    private void SaveStations()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(items));
    }

    private void RefreshStations()
    {
        stations.DataSource = null;
        stations.DataSource = items;
        stations.DisplayMember = nameof(Station.Name);
    }

    private void DrawStation(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var station = (Station)stations.Items[e.Index]!;
        using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(57, 60, 48) : Color.FromArgb(45, 45, 45));
        e.Graphics.FillRectangle(background, e.Bounds);
        using var title = new Font(Font, FontStyle.Bold);
        e.Graphics.DrawString(station.Name, title, Brushes.White, e.Bounds.X + 16, e.Bounds.Y + 11);
        e.Graphics.DrawString(station.Artist, Font, Brushes.Silver, e.Bounds.X + 16, e.Bounds.Y + 35);
    }

    private void DrawEqualizer(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(equalizer.BackColor);
        for (var i = 0; i < 30; i++)
        {
            var height = playing ? Random.Shared.Next(8, equalizer.Height - 10) : 4;
            e.Graphics.FillRectangle(Brushes.Red, 12 + i * 22, equalizer.Height - height - 6, 12, height);
        }
    }

    private static Button Button(string text) => new() { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, FlatAppearance = { BorderColor = Color.FromArgb(65, 65, 65) } };
    private static string Short(string value) => value.Replace(Environment.NewLine, " ")[..Math.Min(value.Length, 100)];

    private sealed class Station
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
    }
}
