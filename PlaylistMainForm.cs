using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CatLuYNet.Core;
using LibVLCSharp.Shared;

namespace YouTubeRadio;

public sealed class PlaylistMainForm : Form
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
    private readonly Panel visualizer = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = false };
    private readonly Panel catVideoHost = new() { Dock = DockStyle.Right, Width = 160, BackColor = Color.Black, Visible = false };
    private readonly LibVLC catVlc;
    private readonly MediaPlayer catPlayer;
    private Media? catMedia;
    private RowStyle? visualizerRow;
    private RowStyle? bottomRow;
    private int visualizerPhase;
    private readonly HttpClient http = new();
    private Button? settingsButton;
    private Form? miniPlayerForm;
    private Label? miniTrackTitle;
    private Button? miniPlayButton;
    private TrackBar? miniVolume;
    private bool restoringMiniPlayer;
    private Label? updateBadge;
    private string? updateUrl;
    private bool checkingUpdate;
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
        var header = new Label { Text = "CatLu YNet 1.1.2", Dock = DockStyle.Fill, Padding = new Padding(5), Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, BackColor = surfaceColor, TextAlign = ContentAlignment.MiddleLeft, Tag = "header" };
        var appIcon = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(8), BackColor = surfaceColor, Tag = "application-icon" };
        settingsButton = Button("⚙", surfaceColor); settingsButton.Size = new Size(36, 36); settingsButton.Margin = Padding.Empty; settingsButton.Dock = DockStyle.None; settingsButton.Tag = "header-action"; settingsButton.TabStop = false;
        settingsButton.FlatAppearance.MouseOverBackColor = surfaceColor; settingsButton.FlatAppearance.MouseDownBackColor = surfaceColor;
        updateBadge = new Label { Text = "1", Visible = false, AutoSize = false, Width = 18, Height = 18, Left = 27, Top = 3, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(229, 9, 20), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        var settingsSlot = new Panel { Dock = DockStyle.Right, Width = 96, BackColor = surfaceColor };
        mini.Location = new Point(8, 8); settingsButton.Location = new Point(52, 8);
        settingsSlot.Controls.AddRange([mini, settingsButton, updateBadge]);
        var headerBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = surfaceColor };
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        headerBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headerBar.Controls.Add(appIcon, 0, 0); headerBar.Controls.Add(header, 1, 0); headerBar.Controls.Add(settingsSlot, 2, 0);
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
        input.Controls.Add(save, 1, 0);
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

        Load += async (_, _) => { LoadLibrary(); if (settings.MusicLights) StartDancingCat(); await CheckLinkedPlaylistsAsync(); await CheckForUpdateAsync(false); };
        FormClosing += (_, e) => { if (libraryLoaded && !SaveLibrary()) e.Cancel = true; };
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
            infoTimer.Dispose(); visualizerTimer.Dispose(); crossfadeTimer.Dispose();
            catPlayer.Dispose(); catVlc.Dispose(); player.Dispose(); vlc.Dispose(); http.Dispose();
            playbackRequest.Dispose(); lifetime.Dispose();
            trackFont.Dispose(); playlistFont.Dispose(); editFont.Dispose(); linkFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ShowMiniPlayer()
    {
        if (miniPlayerForm is not null) { miniPlayerForm.Activate(); return; }

        var miniForm = new Form
        {
            Text = "CatLu YNet — мини-плеер", Icon = Icon, StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedToolWindow, ClientSize = new Size(430, 104),
            MinimumSize = new Size(430, 104), MaximumSize = new Size(430, 104),
            BackColor = Color.White, ForeColor = Color.FromArgb(25, 25, 25), ShowInTaskbar = false, TopMost = true
        };
        var cat = new Panel { Dock = DockStyle.Left, Width = 112, BackColor = Color.White, Padding = Padding.Empty };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(7, 3, 7, 3), BackColor = Color.White };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        miniTrackTitle = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(25, 25, 25), Text = "Ничего не воспроизводится" };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 1, 0, 0), Margin = Padding.Empty, BackColor = Color.White };
        var previous = MiniButton("◀◀", Color.FromArgb(230, 230, 230));
        miniPlayButton = MiniButton(paused || !player.IsPlaying ? "▶" : "❚❚", accentColor);
        var stop = MiniButton("■", Color.FromArgb(230, 230, 230));
        var next = MiniButton("▶▶", Color.FromArgb(230, 230, 230));
        var restore = MiniButton("↗", Color.FromArgb(230, 230, 230)); restore.AccessibleName = "Развернуть"; new ToolTip().SetToolTip(restore, "Развернуть");
        miniVolume = new TrackBar { Minimum = 0, Maximum = 100, Value = volume.Value, TickStyle = TickStyle.None, Width = 84, Height = 28, Margin = new Padding(2, 0, 0, 0) };
        buttons.Controls.AddRange([previous, miniPlayButton, stop, next, restore, miniVolume]);
        content.Controls.Add(miniTrackTitle, 0, 0);
        content.Controls.Add(buttons, 0, 1);
        miniForm.Controls.Add(content);
        miniForm.Controls.Add(cat);

        catPlayer.Stop();
        catMedia?.Dispose();
        catMedia = null;
        catVideoHost.Parent?.Controls.Remove(catVideoHost);
        cat.Controls.Add(catVideoHost);
        catVideoHost.Dock = DockStyle.None;
        catVideoHost.Size = new Size(112, 63);
        catVideoHost.Location = new Point(0, (miniForm.ClientSize.Height - catVideoHost.Height) / 2);
        catVideoHost.BackColor = Color.White;
        catVideoHost.Visible = true;
        miniPlayerForm = miniForm;
        miniVolume.Scroll += (_, _) => SetPlaybackVolume(miniVolume.Value);
        previous.Click += async (_, _) => await PreviousAsync();
        miniPlayButton.Click += async (_, _) =>
        {
            if (paused || !player.IsPlaying) await PlayCurrentAsync(); else PausePlayback();
            UpdateMiniPlayer();
        };
        stop.Click += (_, _) => { Stop(); status.Text = "Остановлено"; UpdateMiniPlayer(); };
        next.Click += async (_, _) => await NextAsync();
        restore.Click += (_, _) => RestoreMainPlayer();
        miniForm.FormClosing += (_, e) =>
        {
            if (restoringMiniPlayer || closing) return;
            e.Cancel = true;
            RestoreMainPlayer();
        };
        miniForm.Show();
        StartDancingCat();
        UpdateMiniPlayer();
        Hide();
    }

    private void RestoreMainPlayer()
    {
        if (miniPlayerForm is null) return;
        restoringMiniPlayer = true;
        catPlayer.Stop();
        catVideoHost.Parent?.Controls.Remove(catVideoHost);
        visualizer.Controls.Add(catVideoHost);
        catVideoHost.Dock = DockStyle.Right;
        catVideoHost.Width = 160;
        catVideoHost.BackColor = Color.Black;
        miniPlayerForm.Close();
        miniPlayerForm.Dispose();
        miniPlayerForm = null;
        miniTrackTitle = null;
        miniPlayButton = null;
        miniVolume = null;
        restoringMiniPlayer = false;
        Show();
        Activate();
        if (settings.MusicLights) StartDancingCat(); else StopDancingCat();
    }

    private void UpdateMiniPlayer()
    {
        if (miniTrackTitle is not null)
            miniTrackTitle.Text = currentTrack is null ? "Ничего не воспроизводится" : $"{currentTrack.Name} — {currentTrack.Artist}";
        if (miniPlayButton is not null) miniPlayButton.Text = paused || !player.IsPlaying ? "▶" : "❚❚";
    }

    private static Button MiniButton(string text, Color color) => new()
    {
        Text = text, Size = new Size(38, 30), Margin = new Padding(2, 1, 2, 1), Padding = Padding.Empty,
        FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = color,
        ForeColor = color == Color.FromArgb(229, 9, 20) ? Color.White : Color.FromArgb(25, 25, 25),
        Font = new Font("Segoe UI", 9, FontStyle.Bold), UseVisualStyleBackColor = false
    };

    private void SetPlaybackVolume(int value)
    {
        value = Math.Clamp(value, 0, 100);
        volume.Value = value;
        if (miniVolume is not null) miniVolume.Value = value;
        player.Mute = false;
        player.Volume = value;
    }

    private async Task SaveLinkAsync()
    {
        if (!IsYouTubeUrl(urlBox.Text, out var isPlaylist)) { status.Text = "Введите ссылку YouTube"; return; }
        try
        {
            status.Text = "Чтение YouTube...";
            if (isPlaylist)
            {
                var title = Ask("Сохранить плейлист", "Название плейлиста:");
                if (string.IsNullOrWhiteSpace(title)) return;
                var sourceUrl = urlBox.Text.Trim();
                var playlist = new Playlist { Name = title, Expanded = true, SourceUrl = sourceUrl, LastCheckedUtc = DateTime.UtcNow };
                playlist.Tracks.AddRange(await ReadPlaylistAsync(sourceUrl));
                playlists.Add(playlist);
            }
            else
            {
                var playlist = SelectPlaylist();
                if (playlist is null) return;
                var track = await ReadTrackAsync(urlBox.Text);
                if (playlist.Tracks.All(item => item.Id != track.Id)) playlist.Tracks.Add(track);
                playlist.Expanded = true;
            }
            if (closing || !SaveLibrary()) return;
            urlBox.Clear();
            status.Text = "Сохранено";
            RenderPlaylists();
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { if (!closing) status.Text = $"Ошибка: {Short(ex.Message)}"; }
    }

    private void CreatePlaylist()
    {
        var title = Ask("Новый плейлист", "Название плейлиста:");
        if (string.IsNullOrWhiteSpace(title)) return;
        playlists.Add(new Playlist { Name = title, Expanded = true });
        SaveLibrary();
        RenderPlaylists();
    }

    private Playlist? SelectPlaylist()
    {
        using var dialog = new Form { Text = "Выберите плейлист", Size = new Size(420, 420), StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(35, 35, 35), ForeColor = Color.White, MinimizeBox = false, MaximizeBox = false };
        var list = new ListBox { Dock = DockStyle.Fill, DataSource = playlists, DisplayMember = nameof(Playlist.Name), BackColor = Color.FromArgb(35, 35, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11) };
        var select = Button("Сохранить", Color.FromArgb(229, 9, 20));
        var create = Button("＋ Новый", Color.FromArgb(55, 55, 55));
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(5) };
        footer.Controls.AddRange([create, select]);
        dialog.Controls.Add(list);
        dialog.Controls.Add(footer);
        Playlist? selected = null;
        select.Click += (_, _) => { selected = list.SelectedItem as Playlist; if (selected is not null) dialog.Close(); };
        create.Click += (_, _) => { var title = Ask("Новый плейлист", "Название плейлиста:"); if (!string.IsNullOrWhiteSpace(title)) { selected = new Playlist { Name = title, Expanded = true }; playlists.Add(selected); dialog.Close(); } };
        dialog.ShowDialog(this);
        return selected;
    }

    private void RenderPlaylists(bool focusCurrent = false)
    {
        if (closing) return;
        playlistHost.SuspendLayout();
        while (playlistHost.Controls.Count > 0) playlistHost.Controls[0].Dispose();
        Panel? activeRow = null;
        var width = Math.Max(420, playlistHost.ClientSize.Width - 30);
        foreach (var playlist in playlists)
        {
            var rowHeights = playlist.Tracks.Select(track => TrackHeight(track, width)).ToList();
            var card = new Panel { Width = width, Height = playlist.Expanded ? 48 + rowHeights.Sum() : 48, BackColor = cardColor, Margin = new Padding(0, 0, 0, 5) };
            var headerRow = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = surfaceColor };
            var header = new Button { Text = $"{(playlist.Expanded ? "⌄" : "›")}   {playlist.Name}    {playlist.Tracks.Count}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = headerRow.BackColor, ForeColor = textColor, Font = playlistFont, Padding = new Padding(5, 0, 0, 0) };
            var playlistActions = new TableLayoutPanel { Dock = DockStyle.Right, Width = 76, ColumnCount = 2, RowCount = 1, BackColor = headerRow.BackColor };
            playlistActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); playlistActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            var editPlaylist = Button("✎", Color.Transparent);
            var deletePlaylist = Button("×", Color.Transparent);
            foreach (var icon in new[] { editPlaylist, deletePlaylist })
            {
                icon.AutoSize = false; icon.Dock = DockStyle.Fill; icon.Margin = new Padding(4); icon.Padding = Padding.Empty;
                icon.Font = editFont; icon.BackColor = Color.Transparent;
                icon.FlatAppearance.MouseOverBackColor = Color.Transparent;
                icon.FlatAppearance.MouseDownBackColor = Color.Transparent;
            }
            editPlaylist.ForeColor = mutedColor;
            deletePlaylist.ForeColor = accentColor;
            header.Click += (_, _) => { playlist.Expanded = !playlist.Expanded; SaveLibrary(); RenderPlaylists(); };
            editPlaylist.Click += (_, _) => EditPlaylist(playlist);
            deletePlaylist.Click += (_, _) =>
            {
                if (MessageBox.Show($"Удалить плейлист «{playlist.Name}»?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    DeletePlaylist(playlist);
            };
            playlistActions.Controls.Add(editPlaylist, 0, 0); playlistActions.Controls.Add(deletePlaylist, 1, 0);
            headerRow.Controls.Add(header);
            headerRow.Controls.Add(playlistActions);
            card.Controls.Add(headerRow);
            if (playlist.Expanded)
            {
                var top = 48;
                for (var i = 0; i < playlist.Tracks.Count; i++)
                {
                    var row = AddTrackRow(card, playlist, playlist.Tracks[i], top, rowHeights[i]);
                    if (ReferenceEquals(playlist, currentPlaylist) && ReferenceEquals(playlist.Tracks[i], currentTrack)) activeRow = row;
                    top += rowHeights[i];
                }
            }
            playlistHost.Controls.Add(card);
        }
        playlistHost.ResumeLayout();
        if (focusCurrent && activeRow is not null && IsHandleCreated) BeginInvoke(() => { if (!closing && !activeRow.IsDisposed) playlistHost.ScrollControlIntoView(activeRow); });
    }

    private Panel AddTrackRow(Panel card, Playlist playlist, Track track, int top, int height)
    {
        var active = ReferenceEquals(playlist, currentPlaylist) && ReferenceEquals(track, currentTrack);
        var activeColor = Color.FromArgb((cardColor.R + accentColor.R) / 2, (cardColor.G + accentColor.G) / 2, (cardColor.B + accentColor.B) / 2);
        var row = new Panel { Left = 0, Top = top, Width = card.Width, Height = height, BackColor = active ? activeColor : cardColor, BorderStyle = active ? BorderStyle.FixedSingle : BorderStyle.None, Cursor = Cursors.Hand };
        var titleHeight = TextRenderer.MeasureText(track.Name, trackFont, new Size(row.Width - 10, int.MaxValue), TextFormatFlags.WordBreak).Height;
        var title = new Label { Text = track.IsNew ? $"{track.Name} · {T("newTrack")}" : track.Name, Left = 5, Top = 5, Width = row.Width - 10, Height = titleHeight, ForeColor = track.IsNew ? accentColor : textColor, Font = trackFont };
        var artist = new Label { Text = track.Artist, Left = 5, Top = 5 + titleHeight, Width = row.Width - 10, Height = 18, ForeColor = mutedColor };
        Label? source = null;
        if (settings.ShowYoutubeLinks) source = new Label { Text = $"https://www.youtube.com/watch?v={track.Id}", Left = 5, Top = artist.Bottom, Width = row.Width - 10, Height = 18, ForeColor = accentColor, Font = linkFont, Cursor = Cursors.Hand };
        EventHandler play = async (_, _) => { Stop(); currentPlaylist = playlist; currentTrack = track; await PlayCurrentAsync(); };
        row.Click += play; title.Click += play; artist.Click += play;
        if (source is not null) source.Click += (_, _) => Process.Start(new ProcessStartInfo(source.Text) { UseShellExecute = true });
        row.Controls.Add(title); row.Controls.Add(artist); if (source is not null) row.Controls.Add(source); card.Controls.Add(row);
        return row;
    }

    private int TrackHeight(Track track, int width)
    {
        var displayName = track.IsNew ? $"{track.Name} · {T("newTrack")}" : track.Name;
        var measured = TextRenderer.MeasureText(displayName, trackFont, new Size(Math.Max(120, width - 10), int.MaxValue), TextFormatFlags.WordBreak);
        return Math.Max(44, measured.Height + 28 + (settings.ShowYoutubeLinks ? 18 : 0));
    }

    private void EditPlaylist(Playlist playlist)
    {
        using var dialog = new Form { Text = $"Плейлист: {playlist.Name}", Size = new Size(620, 540), StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(25, 25, 25), ForeColor = Color.White, Padding = new Padding(5), MinimizeBox = false, MaximizeBox = false };
        var name = new TextBox { Text = playlist.Name, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11) };
        var tracks = new ListBox { Dock = DockStyle.Fill, Margin = Padding.Empty, DrawMode = DrawMode.OwnerDrawVariable, BackColor = Color.FromArgb(35, 35, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), IntegralHeight = false, HorizontalScrollbar = false };
        var link = new TextBox { Dock = DockStyle.Fill, Margin = Padding.Empty, PlaceholderText = "Ссылка YouTube для добавления", BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var rename = Button("Переименовать", Color.FromArgb(55, 55, 55));
        var add = Button("Добавить", Color.FromArgb(229, 9, 20));
        var up = Button("↑", Color.FromArgb(55, 55, 55));
        var down = Button("↓", Color.FromArgb(55, 55, 55));
        var linkPlaylist = Button(T("linkPlaylist"), Color.FromArgb(55, 55, 55));
        var remove = Button("Удалить", Color.FromArgb(229, 9, 20));
        var removePlaylist = Button("Удалить весь плейлист", Color.FromArgb(229, 9, 20));
        var close = Button("Готово", Color.FromArgb(55, 55, 55));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0) };
        actions.Controls.AddRange([up, down, linkPlaylist, remove, removePlaylist, close]);
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = Padding.Empty };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var nameRow = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, Padding = new Padding(0, 0, 0, 5) };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nameRow.Controls.Add(name, 0, 0);
        nameRow.Controls.Add(rename, 1, 0);
        var linkRow = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, Padding = new Padding(0, 0, 0, 5) };
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        linkRow.Controls.Add(link, 0, 0);
        linkRow.Controls.Add(add, 1, 0);
        rename.Margin = new Padding(5, 0, 0, 0);
        add.Margin = new Padding(5, 0, 0, 0);
        editor.Controls.Add(nameRow, 0, 0);
        editor.Controls.Add(linkRow, 0, 1);
        editor.Controls.Add(tracks, 0, 2);
        editor.Controls.Add(actions, 0, 3);
        dialog.Controls.Add(editor);
        void RefreshTracks() { tracks.DataSource = null; tracks.DataSource = playlist.Tracks; tracks.DisplayMember = nameof(Track.Name); }
        tracks.MeasureItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= playlist.Tracks.Count) return;
            var size = TextRenderer.MeasureText(playlist.Tracks[e.Index].Name, tracks.Font, new Size(Math.Max(120, tracks.ClientSize.Width - 52), int.MaxValue), TextFormatFlags.WordBreak);
            e.ItemHeight = Math.Max(62, size.Height + 34);
        };
        tracks.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= playlist.Tracks.Count) return;
            var track = playlist.Tracks[e.Index];
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? Color.FromArgb(75, 15, 20) : Color.FromArgb(42, 42, 42));
            e.Graphics.FillRectangle(background, e.Bounds);
            var number = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 8, 30, e.Bounds.Height - 16);
            var title = new Rectangle(e.Bounds.X + 42, e.Bounds.Y + 7, e.Bounds.Width - 52, e.Bounds.Height - 28);
            TextRenderer.DrawText(e.Graphics, $"{e.Index + 1}", tracks.Font, number, Color.FromArgb(180, 180, 180), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(e.Graphics, track.Name, tracks.Font, title, Color.White, TextFormatFlags.WordBreak);
            TextRenderer.DrawText(e.Graphics, track.Artist, Font, new Rectangle(e.Bounds.X + 42, e.Bounds.Bottom - 23, e.Bounds.Width - 52, 18), Color.Silver, TextFormatFlags.EndEllipsis);
        };
        rename.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) { playlist.Name = name.Text.Trim(); dialog.Text = $"Плейлист: {playlist.Name}"; SaveLibrary(); RenderPlaylists(); } };
        remove.Click += (_, _) => { if (tracks.SelectedItem is Track track) { RemoveTrack(playlist, track); RefreshTracks(); SaveLibrary(); RenderPlaylists(); } };
        removePlaylist.Click += (_, _) =>
        {
            if (MessageBox.Show($"Удалить плейлист «{playlist.Name}»?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            DeletePlaylist(playlist);
            dialog.Close();
        };
        up.Click += (_, _) => MoveTrack(playlist, tracks, -1, RefreshTracks);
        down.Click += (_, _) => MoveTrack(playlist, tracks, 1, RefreshTracks);
        linkPlaylist.Click += async (_, _) =>
        {
            var sourceUrl = Ask("Связать с YouTube-плейлистом", "Ссылка на плейлист YouTube:");
            if (string.IsNullOrWhiteSpace(sourceUrl)) return;
            if (!IsYouTubeUrl(sourceUrl, out var isPlaylist) || !isPlaylist)
            {
                MessageBox.Show("Введите ссылку на плейлист YouTube.", "Неверная ссылка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                linkPlaylist.Enabled = false;
                var tracksFromSource = await ReadPlaylistAsync(sourceUrl.Trim());
                if (closing || dialog.IsDisposed) return;
                playlist.SourceUrl = sourceUrl.Trim();
                var added = PlaylistStore.PrependMissingTracks(playlist, tracksFromSource);
                playlist.LastCheckedUtc = DateTime.UtcNow;
                if (!SaveLibrary()) return;
                RefreshTracks(); RenderPlaylists();
                status.Text = $"Новых песен добавлено в начало: {added}";
            }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception ex) { if (!closing && !dialog.IsDisposed) MessageBox.Show(Short(ex.Message), "Ошибка проверки плейлиста", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (!linkPlaylist.IsDisposed) linkPlaylist.Enabled = true; }
        };
        add.Click += async (_, _) =>
        {
            if (!IsYouTubeUrl(link.Text, out var isPlaylist) || isPlaylist) return;
            try
            {
                add.Enabled = false;
                var track = await ReadTrackAsync(link.Text);
                if (closing || dialog.IsDisposed || !playlists.Contains(playlist)) return;
                if (playlist.Tracks.All(item => item.Id != track.Id)) playlist.Tracks.Add(track);
                link.Clear(); RefreshTracks(); SaveLibrary(); RenderPlaylists();
            }
            catch (OperationCanceledException) when (closing) { }
            catch (Exception ex) { if (!closing && !dialog.IsDisposed) MessageBox.Show(Short(ex.Message), "Ошибка добавления", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (!add.IsDisposed) add.Enabled = true; }
        };
        close.Click += (_, _) => dialog.Close();
        RefreshTracks();
        dialog.ShowDialog(this);
    }

    private void RemoveTrack(Playlist playlist, Track track)
    {
        if (ReferenceEquals(currentPlaylist, playlist) &&
            (ReferenceEquals(currentTrack, track) || ReferenceEquals(incomingTrack, track) ||
             (isCrossfading && ReferenceEquals(GetNextTrack(), track))))
        {
            Stop();
            if (ReferenceEquals(currentTrack, track)) currentTrack = null;
        }
        playlist.Tracks.Remove(track);
    }

    private void DeletePlaylist(Playlist playlist)
    {
        if (!playlists.Remove(playlist)) return;
        if (ReferenceEquals(currentPlaylist, playlist)) { Stop(); currentTrack = null; currentPlaylist = null; }
        SaveLibrary();
        RenderPlaylists();
    }

    private void MoveTrack(Playlist playlist, ListBox tracks, int offset, Action refresh)
    {
        if (tracks.SelectedItem is not Track track) return;
        var from = playlist.Tracks.IndexOf(track);
        var to = from + offset;
        if (to < 0 || to >= playlist.Tracks.Count) return;
        playlist.Tracks.RemoveAt(from);
        playlist.Tracks.Insert(to, track);
        refresh();
        tracks.SelectedItem = track;
        SaveLibrary();
        RenderPlaylists();
    }

    private async Task PlayCurrentAsync()
    {
        if (closing) return;
        if (paused)
        {
            paused = false;
            player.Mute = false;
            if (incomingPlayer is not null) incomingPlayer.Mute = false;
            player.SetPause(false);
            incomingPlayer?.SetPause(false);
            if (incomingPlayer is not null) crossfadeElapsed.Start();
            crossfadeTimer.Start();
            UpdateMiniPlayer();
            return;
        }
        if (player.IsPlaying) return;
        if (currentTrack is null)
        {
            currentPlaylist = playlists.FirstOrDefault(playlist => playlist.Tracks.Count > 0);
            currentTrack = currentPlaylist?.Tracks.FirstOrDefault();
        }
        if (currentTrack is null) return;
        UpdateMiniPlayer();
        Stop();
        var token = playbackRequest.Token;
        currentPlaylist!.Expanded = true;
        RenderPlaylists(true);
        crossfadeAttemptedTrack = null;
        try
        {
            status.Text = "Получение аудиопотока...";
            var output = await RunYtDlpAsync(token, "--no-playlist", "-f", "bestaudio/best", "-g", $"https://www.youtube.com/watch?v={currentTrack.Id}");
            token.ThrowIfCancellationRequested();
            var url = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("yt-dlp не вернул поток.");
            media = new Media(vlc, new Uri(url));
            if (settings.NormalizeAudio) media.AddOption(":audio-filter=compressor");
            SetPlaybackVolume(volume.Value);
            if (!player.Play(media)) throw new InvalidOperationException("LibVLC не открыл поток.");
            crossfadeTimer.Start();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!closing && !token.IsCancellationRequested) status.Text = $"Ошибка: {Short(ex.Message)}"; }
    }

    private void PausePlayback()
    {
        if (paused) return;
        if (media is null) { Stop(); status.Text = "Пауза"; return; }
        paused = true;
        player.SetPause(true);
        incomingPlayer?.SetPause(true);
        crossfadeElapsed.Stop();
        crossfadeTimer.Stop();
        status.Text = "Пауза";
        UpdateMiniPlayer();
    }

    private async Task NextAsync()
    {
        if (currentPlaylist is null || currentTrack is null) return;
        Stop();
        currentTrack = GetNextTrack();
        UpdateMiniPlayer();
        RenderPlaylists(true);
        if (currentTrack is null) return;
        await PlayCurrentAsync();
    }

    private async Task PreviousAsync()
    {
        if (currentPlaylist is null || currentTrack is null || currentPlaylist.Tracks.Count == 0) return;
        Stop();
        var index = currentPlaylist.Tracks.IndexOf(currentTrack);
        currentTrack = currentPlaylist.Tracks[(index - 1 + currentPlaylist.Tracks.Count) % currentPlaylist.Tracks.Count];
        UpdateMiniPlayer();
        await PlayCurrentAsync();
    }

    private Track? GetNextTrack()
    {
        if (currentPlaylist is null || currentPlaylist.Tracks.Count == 0) return null;
        var index = currentPlaylist!.Tracks.IndexOf(currentTrack!);
        return currentPlaylist.Tracks[(index + 1) % currentPlaylist.Tracks.Count];
    }

    private void AttachPlayerEvents(MediaPlayer target)
    {
        target.Playing += (_, _) => PostPlayerEvent(target, () =>
        {
            if (currentTrack?.IsNew == true) { currentTrack.IsNew = false; SaveLibrary(); RenderPlaylists(); }
            status.Text = paused ? "Пауза" : $"Играет: {currentTrack?.Name}";
            UpdateMiniPlayer();
        });
        target.Paused += (_, _) => PostPlayerEvent(target, () => { status.Text = "Пауза"; UpdateMiniPlayer(); });
        target.EncounteredError += (_, _) => PostPlayerEvent(target, () => status.Text = "Ошибка аудиопотока");
        target.EndReached += (_, _) => PostPlayerEnded(target);
    }

    private void PostPlayerEnded(MediaPlayer target)
    {
        if (closing || !IsHandleCreated) return;
        var request = playbackRequest;
        try
        {
            BeginInvoke((Action)(async () =>
            {
                if (closing || paused || !ReferenceEquals(request, playbackRequest)) return;
                if (ReferenceEquals(target, incomingPlayer))
                {
                    CompleteCrossfade();
                    await NextAsync();
                }
                else if (ReferenceEquals(target, player))
                {
                    if (isCrossfading)
                    {
                        currentTrackEnded = true;
                        if (incomingPlayer is not null) CompleteCrossfade();
                    }
                    else await NextAsync();
                }
            }));
        }
        catch (InvalidOperationException) when (closing || !IsHandleCreated) { }
    }

    private void PostPlayerEvent(MediaPlayer target, Action action)
    {
        if (closing || !IsHandleCreated || !ReferenceEquals(target, player)) return;
        var request = playbackRequest;
        try
        {
            BeginInvoke(() => { if (!closing && ReferenceEquals(request, playbackRequest) && ReferenceEquals(target, player)) action(); });
        }
        catch (InvalidOperationException) when (closing || !IsHandleCreated) { }
    }

    private async Task HandleCrossfadeAsync()
    {
        if (closing || paused) return;
        if (isCrossfading)
        {
            if (incomingPlayer is null) return;
            var progress = Math.Min(1d, crossfadeElapsed.Elapsed.TotalMilliseconds / 5000d);
            player.Volume = (int)Math.Round(volume.Value * (1d - progress));
            incomingPlayer.Volume = (int)Math.Round(volume.Value * progress);
            if (progress >= 1d) CompleteCrossfade();
            return;
        }

        if (!settings.Crossfade || currentPlaylist is null || currentTrack is null || currentPlaylist.Tracks.Count < 2 || player.Length <= 0 || player.Time <= 0 || ReferenceEquals(crossfadeAttemptedTrack, currentTrack)) return;
        if (player.Length - player.Time <= 5000) await StartCrossfadeAsync();
    }

    private async Task StartCrossfadeAsync()
    {
        var token = playbackRequest.Token;
        isCrossfading = true;
        crossfadeAttemptedTrack = currentTrack;
        try
        {
            var next = GetNextTrack();
            if (next is null) { isCrossfading = false; return; }
            var output = await RunYtDlpAsync(token, "--no-playlist", "-f", "bestaudio/best", "-g", $"https://www.youtube.com/watch?v={next.Id}");
            token.ThrowIfCancellationRequested();
            var url = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("yt-dlp не вернул следующий поток.");
            incomingMedia = new Media(vlc, new Uri(url));
            if (settings.NormalizeAudio) incomingMedia.AddOption(":audio-filter=compressor");
            incomingPlayer = new MediaPlayer(vlc) { Volume = 0, Mute = false };
            AttachPlayerEvents(incomingPlayer);
            if (!incomingPlayer.Play(incomingMedia)) throw new InvalidOperationException("LibVLC не открыл следующий поток.");
            incomingTrack = next;
            crossfadeElapsed.Restart();
            if (paused) { incomingPlayer.SetPause(true); crossfadeElapsed.Stop(); }
            else if (currentTrackEnded) CompleteCrossfade();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (closing || token.IsCancellationRequested) return;
            incomingPlayer?.Dispose(); incomingPlayer = null;
            incomingMedia?.Dispose(); incomingMedia = null;
            isCrossfading = false;
            status.Text = $"Кроссфейд: {Short(ex.Message)}";
            if (!paused && currentTrackEnded) await NextAsync();
        }
    }

    private void CompleteCrossfade()
    {
        var outgoingPlayer = player;
        player = incomingPlayer!;
        outgoingPlayer.Stop();
        media?.Dispose();
        media = incomingMedia;
        currentTrack = incomingTrack;
        if (currentTrack?.IsNew == true) { currentTrack.IsNew = false; SaveLibrary(); }
        currentPlaylist!.Expanded = true;
        incomingPlayer = null; incomingMedia = null; incomingTrack = null;
        player.Volume = volume.Value;
        isCrossfading = false;
        currentTrackEnded = false;
        crossfadeAttemptedTrack = null;
        outgoingPlayer.Dispose();
        RenderPlaylists(true);
        status.Text = $"Играет: {currentTrack?.Name}";
        UpdateMiniPlayer();
    }

    private void Stop()
    {
        playbackRequest.Cancel();
        paused = false;
        crossfadeElapsed.Reset();
        crossfadeTimer.Stop();
        isCrossfading = false;
        currentTrackEnded = false;
        incomingPlayer?.Stop(); incomingPlayer?.Dispose(); incomingPlayer = null;
        incomingMedia?.Dispose(); incomingMedia = null; incomingTrack = null;
        player.Stop(); media?.Dispose(); media = null;
        playbackRequest.Dispose();
        playbackRequest = new CancellationTokenSource();
    }

    private async Task<Track> ReadTrackAsync(string url)
    {
        using var document = JsonDocument.Parse(await RunYtDlpAsync(lifetime.Token, "--no-playlist", "--dump-single-json", CleanVideoUrl(url)));
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
    private static bool IsVideoId(string? id) => YouTubeUrl.IsVideoId(id);
    private static string CleanVideoUrl(string value) => YouTubeUrl.CleanVideoUrl(value);

    private static string? GetVideoId(Uri uri)
    {
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) return uri.AbsolutePath.Trim('/');
        var queryId = GetQueryValue(uri, "v");
        if (!string.IsNullOrWhiteSpace(queryId)) return queryId;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        return parts.Length == 2 && (parts[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("embed", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("live", StringComparison.OrdinalIgnoreCase)) ? parts[1] : null;
    }

    private static string? GetQueryValue(Uri uri, string name) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)).FirstOrDefault(part => part.Length == 2 && part[0].Equals(name, StringComparison.OrdinalIgnoreCase)) is { } pair ? Uri.UnescapeDataString(pair[1]) : null;
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
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            throw;
        }
        await Task.WhenAll(output, error).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return (output.Result, error.Result, process.ExitCode);
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
                updateUrl = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(asset => asset.GetProperty("name").GetString()?.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) == true).GetProperty("browser_download_url").GetString();
                if (!string.IsNullOrWhiteSpace(updateUrl)) updateBadge!.Visible = true;
                if (showResult && MessageBox.Show($"Доступна версия {newest}. Скачать установщик?", T("updates"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Process.Start(new ProcessStartInfo(updateUrl!) { UseShellExecute = true });
            }
            else if (showResult) MessageBox.Show(T("upToDate"), T("updates"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (showResult && !closing) MessageBox.Show($"{T("updateError")}: {Short(ex.Message)}", T("updates"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { checkingUpdate = false; }
    }

    private void ShowSettings()
    {
        using var dialog = new Form { Text = T("settings"), Size = new Size(430, 540), StartPosition = FormStartPosition.CenterParent, BackColor = surfaceColor, ForeColor = textColor, Padding = new Padding(5), MinimizeBox = false, MaximizeBox = false };
        var theme = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        theme.Items.AddRange(["Системная", "Тёмная", "Светлая", "Apple", "YouTube", "Spotify", "Netflix"]); theme.SelectedIndex = Math.Clamp(settings.Theme, 0, 6);
        var language = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        language.Items.AddRange(["Системный", "Русский", "English", "עברית", "Lietuvių"]); language.SelectedIndex = Math.Clamp(settings.Language, 0, 4);
        var showClock = new CheckBox { Text = T("clock"), Checked = settings.ShowClock, AutoSize = true };
        var showInfo = new CheckBox { Text = T("trackInfo"), Checked = settings.ShowTrackInfo, AutoSize = true };
        var normalize = new CheckBox { Text = T("normalize"), Checked = settings.NormalizeAudio, AutoSize = true };
        var crossfade = new CheckBox { Text = T("crossfade"), Checked = settings.Crossfade, AutoSize = true };
        var musicLights = new CheckBox { Text = T("musicLights"), Checked = settings.MusicLights, AutoSize = true };
        var youtubeLinks = new CheckBox { Text = T("youtubeLinks"), Checked = settings.ShowYoutubeLinks, AutoSize = true };
        var checkUpdates = Button(T("checkUpdates"), cardColor); checkUpdates.AutoSize = false; checkUpdates.Size = new Size(235, 32); checkUpdates.Anchor = AnchorStyles.Top | AnchorStyles.Left; checkUpdates.Margin = Padding.Empty; checkUpdates.TextAlign = ContentAlignment.MiddleCenter;
        var save = Button(T("save"), accentColor); save.AutoSize = false; save.Size = new Size(120, 32); save.Anchor = AnchorStyles.Top | AnchorStyles.Right; save.Margin = Padding.Empty; save.TextAlign = ContentAlignment.MiddleCenter;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10, Padding = new Padding(5) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 10; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.Controls.Add(new Label { Text = T("theme"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); grid.Controls.Add(theme, 1, 0);
        grid.Controls.Add(new Label { Text = T("language"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); grid.Controls.Add(language, 1, 1);
        grid.Controls.Add(showClock, 1, 2); grid.Controls.Add(showInfo, 1, 3); grid.Controls.Add(normalize, 1, 4); grid.Controls.Add(crossfade, 1, 5); grid.Controls.Add(musicLights, 1, 6); grid.Controls.Add(youtubeLinks, 1, 7); grid.Controls.Add(checkUpdates, 1, 8); grid.Controls.Add(save, 1, 9);
        dialog.Controls.Add(grid);
        save.Click += (_, _) =>
        {
            settings.Theme = theme.SelectedIndex; settings.Language = language.SelectedIndex; settings.ShowClock = showClock.Checked; settings.ShowTrackInfo = showInfo.Checked; settings.NormalizeAudio = normalize.Checked; settings.Crossfade = crossfade.Checked; settings.MusicLights = musicLights.Checked; settings.ShowYoutubeLinks = youtubeLinks.Checked;
            if (SaveSettings()) { ApplySettings(); dialog.Close(); }
        };
        checkUpdates.Click += async (_, _) => await CheckForUpdateAsync(true);
        dialog.ShowDialog(this);
    }

    private void ApplySettings()
    {
        (pageColor, surfaceColor, cardColor, accentColor, textColor, mutedColor) = settings.Theme switch
        {
            2 => (Color.FromArgb(245, 245, 247), Color.White, Color.FromArgb(232, 232, 235), Color.FromArgb(0, 122, 255), Color.FromArgb(20, 20, 20), Color.FromArgb(90, 90, 95)),
            3 => (Color.FromArgb(242, 242, 247), Color.White, Color.FromArgb(229, 229, 234), Color.FromArgb(0, 122, 255), Color.FromArgb(20, 20, 20), Color.FromArgb(100, 100, 105)),
            4 => (Color.FromArgb(15, 15, 15), Color.FromArgb(30, 30, 30), Color.FromArgb(42, 42, 42), Color.FromArgb(255, 0, 0), Color.White, Color.Silver),
            5 => (Color.FromArgb(18, 18, 18), Color.FromArgb(30, 30, 30), Color.FromArgb(45, 45, 45), Color.FromArgb(29, 185, 84), Color.White, Color.Silver),
            6 => (Color.FromArgb(14, 14, 14), Color.FromArgb(25, 25, 25), Color.FromArgb(45, 45, 45), Color.FromArgb(229, 9, 20), Color.White, Color.Silver),
            1 => (Color.FromArgb(32, 32, 32), Color.FromArgb(45, 45, 45), Color.FromArgb(60, 60, 60), Color.FromArgb(0, 120, 215), Color.White, Color.Silver),
            _ when SystemThemeIsDark() => (Color.FromArgb(32, 32, 32), Color.FromArgb(45, 45, 45), Color.FromArgb(60, 60, 60), Color.FromArgb(0, 120, 215), Color.White, Color.Silver),
            _ => (Color.FromArgb(245, 245, 245), Color.White, Color.FromArgb(230, 230, 230), Color.FromArgb(0, 120, 215), Color.FromArgb(20, 20, 20), Color.FromArgb(90, 90, 90))
        };
        BackColor = pageColor; ApplyColors(this); playlistHost.BackColor = pageColor; clock.Visible = settings.ShowClock;
        visualizer.Visible = settings.MusicLights; visualizerRow!.Height = settings.MusicLights ? 90 : 0; bottomRow!.Height = settings.MusicLights ? 176 : 86; visualizer.Invalidate();
        if (IsHandleCreated) { if (settings.MusicLights || miniPlayerForm is not null) StartDancingCat(); else StopDancingCat(); }
        RightToLeft = CurrentLanguage() == "he" ? RightToLeft.Yes : RightToLeft.No;
        foreach (var (control, key) in localizedControls) control.Text = T(key);
        urlBox.PlaceholderText = T("url"); RenderPlaylists(); UpdateInfo();
    }

    private void ApplyColors(Control control)
    {
        foreach (Control child in control.Controls)
        {
            if (Equals(child.Tag, "header") || Equals(child.Tag, "application-icon")) { child.BackColor = surfaceColor; child.ForeColor = textColor; }
            else if (child is TextBox or ComboBox or ListBox) { child.BackColor = cardColor; child.ForeColor = textColor; }
            else if (child is Button button) { button.BackColor = Equals(button.Tag, "header-action") ? surfaceColor : Equals(button.Tag, "accent") ? accentColor : cardColor; button.ForeColor = textColor; }
            else if (child is Label label)
            {
                if (Equals(label.Tag, "accent") || Equals(label.Tag, "muted") || Equals(label.Tag, "header")) label.BackColor = surfaceColor;
                label.ForeColor = Equals(label.Tag, "accent") ? accentColor : Equals(label.Tag, "muted") ? mutedColor : textColor;
            }
            else if (child is Panel or TableLayoutPanel or FlowLayoutPanel) child.BackColor = surfaceColor;
            ApplyColors(child);
        }
    }

    private void RegisterLocalized(Control control, string key) => localizedControls[control] = key;
    private string CurrentLanguage() => settings.Language switch { 1 => "ru", 2 => "en", 3 => "he", 4 => "lt", _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName };
    private string T(string key) => Translations.TryGetValue(CurrentLanguage(), out var language) && language.TryGetValue(key, out var text) ? text : Translations["ru"][key];
    private void UpdateInfo()
    {
        clock.Text = DateTime.Now.ToString("HH:mm");
        if (settings.ShowTrackInfo && player.IsPlaying && currentTrack is not null) status.Text = $"{T("playing")}: {FormatTime(player.Time)} / {FormatTime(player.Length)} · {currentTrack.Name}";
    }
    private static string FormatTime(long milliseconds)
    {
        if (milliseconds <= 0) return "--:--";
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return time.TotalHours >= 1 ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : time.ToString(@"m\:ss");
    }
    private static bool SystemThemeIsDark() => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;

    private void DrawVisualizer(Graphics graphics, Rectangle area)
    {
        if (catVideoHost.Visible && ReferenceEquals(catVideoHost.Parent, visualizer)) area.Width = Math.Max(0, area.Width - catVideoHost.Width);
        graphics.Clear(surfaceColor);
        var count = 20; var gap = 4; var width = Math.Max(4, (area.Width - gap * (count + 1)) / count);
        var colors = new[] { Color.FromArgb(0, 229, 255), Color.FromArgb(41, 121, 255), Color.FromArgb(124, 77, 255), Color.FromArgb(245, 0, 87), Color.FromArgb(255, 64, 129), Color.FromArgb(118, 255, 3) };
        for (var i = 0; i < count; i++)
        {
            var wave = (Math.Sin((visualizerPhase * 1.35 + i * 3) * 0.58) + Math.Sin((visualizerPhase + i * 5) * 0.31) + 2d) / 4d;
            var height = 8 + (int)((area.Height - 14) * wave);
            using var brush = new SolidBrush(colors[i % colors.Length]);
            graphics.FillRectangle(brush, gap + i * (width + gap), area.Bottom - height - 4, width, height);
        }
        for (var i = 0; i < 10; i++)
        {
            var x = (int)((Math.Sin((visualizerPhase + i * 11) * 0.23) + 1d) * (area.Width - 12) / 2d);
            var y = 4 + (int)((Math.Cos((visualizerPhase + i * 7) * 0.37) + 1d) * Math.Max(1, area.Height - 14) / 2d);
            var size = 3 + (visualizerPhase + i) % 4;
            using var flash = new SolidBrush(Color.FromArgb(210, colors[(i + visualizerPhase / 4) % colors.Length]));
            graphics.FillEllipse(flash, x, y, size, size);
        }
    }

    private void StartDancingCat()
    {
        try
        {
            catVideoHost.Visible = true;
            if (catPlayer.IsPlaying) return;
            var path = Path.Combine(AppContext.BaseDirectory, "Catlu.mp4");
            if (!File.Exists(path)) { status.Text = "Не найден файл Catlu.mp4"; return; }
            catPlayer.Hwnd = catVideoHost.Handle;
            catMedia?.Dispose();
            catMedia = new Media(catVlc, path, FromType.FromPath);
            if (!catPlayer.Play(catMedia)) throw new InvalidOperationException("LibVLC не открыл Catlu.mp4.");
        }
        catch (Exception ex) { catVideoHost.Visible = false; status.Text = $"Не удалось запустить видео кота: {Short(ex.Message)}"; }
    }

    private void StopDancingCat()
    {
        catPlayer.Stop();
        catMedia?.Dispose();
        catMedia = null;
        catVideoHost.Visible = false;
    }

    private void ReplayDancingCat()
    {
        if (closing || !IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)(() =>
            {
                if (!closing && (settings.MusicLights || miniPlayerForm is not null) && catMedia is not null) catPlayer.Play(catMedia);
            }));
        }
        catch (InvalidOperationException) when (closing || !IsHandleCreated) { }
    }

    private AppSettings LoadSettings()
    {
        try { return File.Exists(settingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? new AppSettings() : new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }
    private bool SaveSettings()
    {
        try { AtomicFile.WriteAllText(settingsPath, JsonSerializer.Serialize(settings)); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = $"Настройки не сохранены: {Short(ex.Message)}";
            return false;
        }
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

    private sealed class AppSettings { public int Theme { get; set; } = 0; public int Language { get; set; } = 0; public bool ShowClock { get; set; } = false; public bool ShowTrackInfo { get; set; } = false; public bool NormalizeAudio { get; set; } = true; public bool Crossfade { get; set; } = true; public bool MusicLights { get; set; } = false; public bool ShowYoutubeLinks { get; set; } = false; }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["ru"] = new() { ["save"] = "Сохранить", ["newPlaylist"] = "+ СОЗДАТЬ ПЛЕЙЛИСТ", ["stations"] = "Плейлисты", ["newTrack"] = "НОВОЕ", ["linkPlaylist"] = "Связать YouTube", ["volume"] = "Громкость", ["settings"] = "Настройки", ["theme"] = "Тема", ["language"] = "Язык", ["clock"] = "Показывать часы", ["trackInfo"] = "Информация о треке", ["normalize"] = "Выравнивание звука", ["crossfade"] = "Кроссфейд", ["musicLights"] = "Цветомузыка", ["youtubeLinks"] = "Показывать ссылки YouTube", ["checkUpdates"] = "Проверить обновления", ["updates"] = "Обновления", ["upToDate"] = "Установлена последняя версия.", ["updateError"] = "Не удалось проверить обновления", ["playing"] = "Играет", ["url"] = "Вставьте ссылку на ролик или плейлист YouTube" },
        ["en"] = new() { ["save"] = "Save", ["newPlaylist"] = "+ CREATE PLAYLIST", ["stations"] = "Playlists", ["newTrack"] = "NEW", ["linkPlaylist"] = "Link YouTube", ["volume"] = "Volume", ["settings"] = "Settings", ["theme"] = "Theme", ["language"] = "Language", ["clock"] = "Show clock", ["trackInfo"] = "Track information", ["normalize"] = "Normalize audio", ["crossfade"] = "Crossfade", ["playing"] = "Playing", ["url"] = "Paste a YouTube video or playlist link" },
        ["he"] = new() { ["save"] = "שמור", ["newPlaylist"] = "+ פלייליסט חדש", ["stations"] = "פלייליסטים", ["newTrack"] = "חדש", ["linkPlaylist"] = "קישור YouTube", ["volume"] = "עוצמה", ["settings"] = "הגדרות", ["theme"] = "ערכת נושא", ["language"] = "שפה", ["clock"] = "הצג שעון", ["trackInfo"] = "פרטי רצועה", ["normalize"] = "איזון עוצמה", ["crossfade"] = "מעבר חלק", ["playing"] = "מנגן", ["url"] = "הדביקו קישור YouTube" },
        ["lt"] = new() { ["save"] = "Išsaugoti", ["newPlaylist"] = "+ NAUJAS GROJARAŠTIS", ["stations"] = "Grojaraščiai", ["newTrack"] = "NAUJA", ["linkPlaylist"] = "Susieti YouTube", ["volume"] = "Garsumas", ["settings"] = "Nustatymai", ["theme"] = "Tema", ["language"] = "Kalba", ["clock"] = "Rodyti laikrodį", ["trackInfo"] = "Takelio informacija", ["normalize"] = "Garso lyginimas", ["crossfade"] = "Kryžminis perėjimas", ["playing"] = "Groja", ["url"] = "Įklijuokite YouTube nuorodą" }
    };
}
