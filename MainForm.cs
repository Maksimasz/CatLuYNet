using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

namespace YouTubeRadio;

public sealed class MainForm : Form
{
    private readonly TextBox urlBox = new() { PlaceholderText = "Ссылка на ролик или плейлист YouTube", Dock = DockStyle.Fill };
    private readonly ListBox sources = new() { Dock = DockStyle.Fill, DisplayMember = nameof(SourceItem.Name), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 62, BackColor = Color.FromArgb(31, 31, 31), BorderStyle = BorderStyle.None };
    private readonly Panel equalizer = new() { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.FromArgb(20, 20, 20) };
    private readonly WebView2 webView = new() { Location = Point.Empty, Size = new Size(200, 200) };
    private readonly TrackBar volume = new() { Minimum = 0, Maximum = 100, Value = 70, Width = 170, TickStyle = TickStyle.None };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(12, 3, 0, 0), ForeColor = Color.Silver, Text = "Подготовка YouTube..." };
    private readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 120 };
    private readonly List<SourceItem> items = [];
    private readonly string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeRadio", "state.json");
    private bool ready;
    private bool playing;

    public MainForm()
    {
        Text = "CatLu YNet";
        MinimumSize = new Size(540, 430);
        Size = new Size(720, 520);
        BackColor = Color.FromArgb(31, 31, 31);
        ForeColor = Color.White;

        var add = CreateButton("Добавить");
        var play = CreateButton("▶");
        var pause = CreateButton("❚❚");
        var next = CreateButton("▶▶");
        var remove = CreateButton("Удалить");
        var volumeText = new Label { Text = "Громкость", AutoSize = true, Padding = new Padding(8, 9, 0, 0), ForeColor = Color.Silver };
        var header = new Label { Text = "📺  CatLu YNet", Dock = DockStyle.Top, Height = 46, Padding = new Padding(18, 13, 0, 0), Font = new Font(Font, FontStyle.Bold), ForeColor = Color.White };
        var tabs = new Label { Text = "     Станции              Поиск              Избранное              История              Эквалайзер       ⚙", Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 14, 0, 0), ForeColor = Color.FromArgb(190, 190, 190), Font = new Font(Font, FontStyle.Bold) };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 2, Padding = new Padding(10) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(urlBox, 0, 0);
        top.Controls.Add(add, 1, 0);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(10, 6, 10, 6) };
        controls.Controls.AddRange([play, pause, next, volumeText, volume, remove]);
        Controls.Add(sources);
        Controls.Add(equalizer);
        Controls.Add(status);
        Controls.Add(controls);
        Controls.Add(tabs);
        Controls.Add(top);
        Controls.Add(header);
        Controls.Add(webView);
        webView.SendToBack();

        add.Click += async (_, _) => await AddSource();
        play.Click += (_, _) => PlaySelected();
        pause.Click += (_, _) =>
        {
            Send("player?.pauseVideo();");
            playing = false;
            meterTimer.Stop();
            equalizer.Invalidate();
        };
        next.Click += (_, _) => SelectNext();
        remove.Click += (_, _) => RemoveSelected();
        volume.Scroll += (_, _) => Send($"setVolume({volume.Value});");
        sources.MouseClick += (_, _) => PlaySelected();
        sources.DoubleClick += (_, _) => PlaySelected();
        sources.DrawItem += DrawSource;
        meterTimer.Tick += (_, _) => equalizer.Invalidate();
        equalizer.Paint += DrawEqualizer;
        Load += async (_, _) => await InitializePlayer();
        FormClosing += (_, _) => Save();
    }

    private async Task InitializePlayer()
    {
        LoadState();
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" };
        await webView.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, null, options));
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping("player.local", Path.Combine(AppContext.BaseDirectory, "wwwroot"), CoreWebView2HostResourceAccessKind.DenyCors);
        webView.CoreWebView2.WebMessageReceived += (_, e) => status.Text = e.TryGetWebMessageAsString();
        webView.NavigationCompleted += (_, e) =>
        {
            ready = e.IsSuccess;
            status.Text = ready ? "YouTube готов" : $"Ошибка загрузки YouTube: {e.WebErrorStatus}";
        };
        webView.Source = new Uri("https://player.local/player.html");
    }

    private async Task AddSource()
    {
        if (!TryParse(urlBox.Text, out var source))
        {
            MessageBox.Show("Введите ссылку на ролик или плейлист YouTube.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            UseWaitCursor = true;
            if (source.Kind == "playlist")
            {
                var added = 0;
                foreach (var video in await YouTubeMetadata.GetPlaylistVideosAsync(urlBox.Text))
                {
                    var item = new SourceItem { Kind = "video", Id = video.Id, Name = video.Title, Artist = video.Author, Group = source.Id };
                    if (!items.Any(x => x.Id == item.Id)) { items.Add(item); added++; }
                }
                if (added == 0) MessageBox.Show("В плейлисте нет новых доступных роликов.", Text);
            }
            else
            {
                var video = await YouTubeMetadata.GetVideoAsync(urlBox.Text, source.Id);
                source.Name = video.Title;
                source.Artist = video.Author;
                items.RemoveAll(x => x.Id == source.Id);
                items.Add(source);
            }
            RefreshStations();
            urlBox.Clear();
            Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось получить данные YouTube: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; }
    }

    private void PlaySelected()
    {
        if (sources.SelectedItem is not SourceItem source || !ready) return;
        Send($"setVolume({volume.Value});load({JsonSerializer.Serialize(source)});");
        playing = true;
        meterTimer.Start();
    }

    private void SelectNext()
    {
        if (items.Count == 0) return;
        sources.SelectedIndex = (sources.SelectedIndex + 1 + items.Count) % items.Count;
        PlaySelected();
    }

    private void RemoveSelected()
    {
        if (sources.SelectedItem is not SourceItem source) return;
        items.Remove(source);
        RefreshStations();
        Save();
    }

    private void Send(string script) => _ = webView.ExecuteScriptAsync(script);

    private void DrawEqualizer(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(equalizer.BackColor);
        for (var i = 0; i < 30; i++)
        {
            var height = playing ? Random.Shared.Next(8, equalizer.Height - 10) : 4;
            e.Graphics.FillRectangle(Brushes.Red, 12 + i * 22, equalizer.Height - height - 6, 12, height);
        }
    }

    private static Button CreateButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(45, 45, 45),
        ForeColor = Color.White,
        FlatAppearance = { BorderColor = Color.FromArgb(65, 65, 65) }
    };

    private void DrawSource(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var item = (SourceItem)sources.Items[e.Index]!;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Color.FromArgb(57, 60, 48) : Color.FromArgb(45, 45, 45));
        e.Graphics.FillRectangle(background, e.Bounds);
        using var title = new Font(Font, FontStyle.Bold);
        e.Graphics.DrawString(item.Name, title, Brushes.White, e.Bounds.X + 16, e.Bounds.Y + 11);
        e.Graphics.DrawString(item.Artist, Font, Brushes.Silver, e.Bounds.X + 16, e.Bounds.Y + 35);
    }

    private void RefreshStations()
    {
        sources.DataSource = null;
        sources.DataSource = items;
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(statePath)) items.AddRange(JsonSerializer.Deserialize<List<SourceItem>>(File.ReadAllText(statePath)) ?? []);
            RefreshStations();
        }
        catch (JsonException) { }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(items));
    }

    private static bool TryParse(string text, out SourceItem source)
    {
        source = new();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (!uri.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))) return false;
        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => x.Length > 1 ? Uri.UnescapeDataString(x[1]) : "");
        parameters.TryGetValue("list", out var playlist);
        parameters.TryGetValue("v", out var video);
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) video = uri.AbsolutePath.Trim('/');
        if (!string.IsNullOrWhiteSpace(playlist)) source = new SourceItem { Kind = "playlist", Id = playlist, Name = $"Плейлист: {playlist}" };
        else if (!string.IsNullOrWhiteSpace(video) && video.Length == 11) source = new SourceItem { Kind = "video", Id = video, Name = $"Видео: {video}" };
        else return false;
        return true;
    }

    public sealed class SourceItem
    {
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Group { get; set; } = "";
    }
}
