using System.Diagnostics;
using System.Text.Json;
using CatLuYNet.Core;
using LibVLCSharp.Shared;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm : Form
{
    private readonly TextBox urlBox = new() { PlaceholderText = "Вставьте ссылку на ролик или плейлист YouTube", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(42, 42, 42), ForeColor = Color.White, Font = new Font("Segoe UI", 11) };
    private readonly FlowLayoutPanel playlistHost = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(15, 15, 15), Padding = new Padding(5) };
    private readonly NoFocusTrackBar volume = new() { Minimum = 0, Maximum = 100, Value = 70, Width = 160, TickStyle = TickStyle.None };
    private readonly Label status = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(5, 3, 0, 0), ForeColor = Color.Silver, BackColor = Color.FromArgb(20, 20, 20), Text = "Готов", TextAlign = ContentAlignment.MiddleLeft, Tag = "muted" };
    private readonly Label clock = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 5, 0), ForeColor = Color.Silver, Visible = false, Tag = "muted" };
    private readonly List<Playlist> playlists = [];
    private readonly string statePath;
    private readonly string settingsPath;
    private readonly string oldStatePath;
    private AppSettings settings = new();
    private readonly System.Windows.Forms.Timer infoTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer visualizerTimer = new() { Interval = 70 };
    private readonly System.Windows.Forms.Timer updateCheckTimer = new() { Interval = (int)TimeSpan.FromHours(24).TotalMilliseconds };
    private readonly Panel visualizer = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = false };
    private readonly Panel catVideoHost = new() { Dock = DockStyle.Right, Width = 160, BackColor = Color.Black, Visible = false };
    private readonly LibVLC catVlc;
    private readonly MediaPlayer catPlayer;
    private Media? catMedia;
    private RowStyle? visualizerRow;
    private RowStyle? bottomRow;
    private int visualizerPhase;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private Button? settingsButton;
    private Form? miniPlayerForm;
    private Label? miniTrackTitle;
    private Button? miniPlayButton;
    private TrackBar? miniVolume;
    private bool restoringMiniPlayer;
    private LinkLabel? updateLink;
    private string? updateUrl;
    private string? updateVersion;
    private bool checkingUpdate;
    private bool installingUpdate;
    private readonly Dictionary<Control, string> localizedControls = [];
    private Color pageColor = Color.FromArgb(15, 15, 15);
    private Color surfaceColor = Color.FromArgb(20, 20, 20);
    private Color cardColor = Color.FromArgb(42, 42, 42);
    private Color accentColor = Color.FromArgb(229, 9, 20);
    private Color textColor = Color.White;
    private Color mutedColor = Color.Silver;
    private readonly LibVLC vlc;
    private MediaPlayer player;
    private Media? media;
    private readonly System.Windows.Forms.Timer crossfadeTimer = new() { Interval = 100 };
    private MediaPlayer? incomingPlayer;
    private Media? incomingMedia;
    private Track? incomingTrack;
    private Track? crossfadeAttemptedTrack;
    private readonly Stopwatch crossfadeElapsed = new();
    private bool isCrossfading;
    private bool currentTrackEnded;
    private Playlist? currentPlaylist;
    private Track? currentTrack;
    private CancellationTokenSource playbackRequest = new();
    private readonly CancellationTokenSource lifetime = new();
    private bool closing;
    private bool paused;
    private bool libraryLoaded;
    private readonly Font trackFont = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font playlistFont = new("Segoe UI", 11, FontStyle.Bold);
    private readonly Font editFont = new("Segoe UI", 14, FontStyle.Bold);
    private readonly Font linkFont = new("Segoe UI", 8, FontStyle.Underline);

    public PlaylistMainForm() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatLuYNet"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeRadio", "state.json")) { }

    internal PlaylistMainForm(string dataDirectory, string legacyPath)
    {
        statePath = Path.Combine(dataDirectory, "stations.json");
        settingsPath = Path.Combine(dataDirectory, "settings.json");
        oldStatePath = legacyPath;
        Core.Initialize();
        settings = LoadSettings();
        vlc = new LibVLC("--no-video", "--network-caching=1000");
        catVlc = new LibVLC("--no-audio");
        catPlayer = new MediaPlayer(catVlc) { Volume = 0 };
        catPlayer.EndReached += (_, _) => ReplayDancingCat();
        player = new MediaPlayer(vlc) { Volume = volume.Value, Mute = false };
        AttachPlayerEvents(player);

        Text = "CatLu YNet";
        Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "CatLuNet2.ico"));
        MinimumSize = new Size(620, 760);
        Size = new Size(760, 820);
        BackColor = Color.FromArgb(15, 15, 15);
        ForeColor = Color.White;

        var save = Button("Сохранить", accentColor);
        var create = new Label { Text = "+ СОЗДАТЬ ПЛЕЙЛИСТ", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(229, 9, 20), BackColor = Color.FromArgb(20, 20, 20), Cursor = Cursors.Hand, Tag = "accent" };
        var previous = Button("◀◀", Color.FromArgb(42, 42, 42));
        var play = Button("▶", accentColor);
        var pause = Button("❚❚", Color.FromArgb(42, 42, 42));
        var stop = Button("■", Color.FromArgb(42, 42, 42));
        var next = Button("▶▶", Color.FromArgb(42, 42, 42));
        var mini = Button("▣", surfaceColor); mini.Size = new Size(36, 36); mini.Margin = Padding.Empty; mini.Dock = DockStyle.None; mini.Tag = "header-action"; mini.TabStop = false; mini.AccessibleName = "Мини-плеер"; new ToolTip().SetToolTip(mini, "Мини-плеер");
        mini.FlatAppearance.MouseOverBackColor = surfaceColor; mini.FlatAppearance.MouseDownBackColor = surfaceColor;
        var appVersion = typeof(PlaylistMainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        var header = new Label { Text = $"CatLu YNet {appVersion}", AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, BackColor = surfaceColor, Tag = "header" };
        updateLink = new LinkLabel { AutoSize = true, Visible = false, Font = new Font("Segoe UI", 9, FontStyle.Bold), LinkColor = accentColor, ActiveLinkColor = accentColor, VisitedLinkColor = accentColor, BackColor = surfaceColor, Tag = "accent", Cursor = Cursors.Hand };
        var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = surfaceColor, Tag = "header" };
        void PositionHeaderItems()
        {
            header.Location = new Point(5, Math.Max(0, (titleBar.ClientSize.Height - header.Height) / 2));
            updateLink.Location = new Point(header.Right + 12, Math.Max(0, (titleBar.ClientSize.Height - updateLink.Height) / 2));
        }
        titleBar.Controls.AddRange([header, updateLink]);
        titleBar.Resize += (_, _) => PositionHeaderItems();
        var appIcon = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(8), BackColor = surfaceColor, Tag = "application-icon" };
        settingsButton = Button("⚙", surfaceColor); settingsButton.Size = new Size(36, 36); settingsButton.Margin = Padding.Empty; settingsButton.Dock = DockStyle.None; settingsButton.Tag = "header-action"; settingsButton.TabStop = false;
        settingsButton.FlatAppearance.MouseOverBackColor = surfaceColor; settingsButton.FlatAppearance.MouseDownBackColor = surfaceColor;
        var settingsSlot = new Panel { Dock = DockStyle.Right, Width = 96, BackColor = surfaceColor };
        mini.Location = new Point(8, 8); settingsButton.Location = new Point(52, 8);
        settingsSlot.Controls.AddRange([mini, settingsButton]);
        var headerBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = surfaceColor };
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        headerBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headerBar.Controls.Add(appIcon, 0, 0); headerBar.Controls.Add(titleBar, 1, 0); headerBar.Controls.Add(settingsSlot, 2, 0);
        var stationBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(20, 20, 20), Padding = new Padding(5) };
        stationBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stationBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        stationBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var stationTitle = new Label { Text = "Плейлисты", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
        stationBar.Controls.Add(stationTitle, 0, 0);
        stationBar.Controls.Add(create, 1, 0);
        var input = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.FromArgb(15, 15, 15), Padding = new Padding(5) };
        input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.Controls.Add(urlBox, 0, 0);
        input.ColumnCount = 3;
        input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        input.Controls.Add(downloadOnAdd, 1, 0);
        input.Controls.Add(save, 2, 0);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(5), BackColor = Color.FromArgb(20, 20, 20) };
        var volumeTitle = new Label { Text = "Громкость", AutoSize = true, Padding = new Padding(5, 5, 0, 0), ForeColor = Color.Silver, Tag = "muted" };
        controls.Controls.AddRange([previous, play, pause, stop, next, volumeTitle, volume]);
        var controlRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = surfaceColor };
        controlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controlRow.Controls.Add(controls, 0, 0);
        var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = surfaceColor };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        statusRow.Controls.Add(status, 0, 0); statusRow.Controls.Add(clock, 1, 0);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.FromArgb(20, 20, 20) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        visualizerRow = new RowStyle(SizeType.Absolute, 0);
        bottom.RowStyles.Add(visualizerRow);
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        bottom.Controls.Add(visualizer, 0, 0);
        bottom.Controls.Add(controlRow, 0, 1);
        bottom.Controls.Add(statusRow, 0, 2);
        visualizer.Controls.Add(catVideoHost);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, BackColor = BackColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottomRow = new RowStyle(SizeType.Absolute, 86); root.RowStyles.Add(bottomRow);
        root.Controls.Add(headerBar, 0, 0);
        root.Controls.Add(input, 0, 1);
        root.Controls.Add(stationBar, 0, 2);
        root.Controls.Add(playlistHost, 0, 3);
        root.Controls.Add(bottom, 0, 4);
        Controls.Add(root);

        RegisterLocalized(save, "save"); RegisterLocalized(create, "newPlaylist"); RegisterLocalized(stationTitle, "stations"); RegisterLocalized(volumeTitle, "volume");

        Load += async (_, _) => { LoadLibrary(); if (settings.MusicLights) StartDancingCat(); updateCheckTimer.Start(); _ = CheckForUpdateAsync(false); await CheckLinkedPlaylistsAsync(); };
        FormClosing += (_, e) => { if (!installingUpdate && libraryLoaded && !SaveLibrary()) e.Cancel = true; };
        Resize += (_, _) => RenderPlaylists();
        save.Click += async (_, _) => await SaveLinkAsync();
        create.Click += (_, _) => CreatePlaylist();
        previous.Click += async (_, _) => await PreviousAsync();
        play.Click += async (_, _) => await PlayCurrentAsync();
        pause.Click += (_, _) => PausePlayback();
        stop.Click += (_, _) => { Stop(); status.Text = "Остановлено"; };
        next.Click += async (_, _) => await NextAsync();
        mini.Click += (_, _) => ShowMiniPlayer();
        volume.Scroll += (_, _) => SetPlaybackVolume(volume.Value);
        crossfadeTimer.Tick += async (_, _) => await HandleCrossfadeAsync();
        settingsButton.Click += (_, _) => ShowSettings();
        updateLink.LinkClicked += async (_, _) => await DownloadAndInstallUpdateAsync();
        updateCheckTimer.Tick += async (_, _) => await CheckForUpdateAsync(false);
        infoTimer.Tick += (_, _) => UpdateInfo();
        visualizer.Paint += (_, e) => DrawVisualizer(e.Graphics, visualizer.ClientRectangle);
        visualizerTimer.Tick += (_, _) => { if (visualizer.Visible && player.IsPlaying) { visualizerPhase++; visualizer.Invalidate(); } };
        infoTimer.Start();
        visualizerTimer.Start();
        ApplySettings();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !closing)
        {
            closing = true;
            lifetime.Cancel();
            Stop();
            StopDancingCat();
            infoTimer.Dispose(); visualizerTimer.Dispose(); crossfadeTimer.Dispose(); updateCheckTimer.Dispose();
            catPlayer.Dispose(); catVlc.Dispose(); player.Dispose(); vlc.Dispose(); http.Dispose();
            playbackRequest.Dispose(); lifetime.Dispose();
            trackFont.Dispose(); playlistFont.Dispose(); editFont.Dispose(); linkFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void LoadLibrary()
    {
        try
        {
            var loaded = PlaylistStore.Load(statePath, oldStatePath);
            playlists.AddRange(loaded);
            libraryLoaded = true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            libraryLoaded = false;
            status.Text = $"Библиотека не прочитана; сохранение отключено: {Short(ex.Message)}";
        }
        RenderPlaylists();
    }

    private bool SaveLibrary()
    {
        if (!libraryLoaded)
        {
            status.Text = "Сохранение отключено: исходная библиотека не была прочитана. Файл сохранён без изменений.";
            return false;
        }
        try { PlaylistStore.Save(statePath, playlists); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = $"Библиотека не сохранена: {Short(ex.Message)}";
            return false;
        }
    }

    private static Button Button(string text, Color color) => new() { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = color, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Padding = new Padding(5, 4, 5, 4), UseVisualStyleBackColor = false, Tag = color.R > 180 && color.G < 60 ? "accent" : "button" };
    private string? Ask(string title, string label)
    {
        using var dialog = new Form { Text = title, Size = new Size(390, 160), StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(35, 35, 35), ForeColor = Color.White, MinimizeBox = false, MaximizeBox = false };
        var input = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(55, 55, 55), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var text = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        var save = Button("Сохранить", Color.FromArgb(229, 9, 20));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 5, 0, 0) };
        actions.Controls.Add(save);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(5) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(text, 0, 0); layout.Controls.Add(input, 0, 1); layout.Controls.Add(actions, 0, 2);
        dialog.Controls.Add(layout);
        save.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }
    private static string Short(string value) { var clean = value.Replace(Environment.NewLine, " "); return clean[..Math.Min(clean.Length, 110)]; }

    private sealed class NoFocusTrackBar : TrackBar
    {
        protected override bool ShowFocusCues => false;
    }

}
