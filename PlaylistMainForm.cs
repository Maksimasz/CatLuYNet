using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
    private readonly string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatLuYNet", "stations.json");
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatLuYNet", "settings.json");
    private readonly string oldStatePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeRadio", "state.json");
    private AppSettings settings = new();
    private readonly System.Windows.Forms.Timer infoTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer visualizerTimer = new() { Interval = 70 };
    private readonly Panel visualizer = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Visible = false };
    private RowStyle? visualizerRow;
    private RowStyle? bottomRow;
    private int visualizerPhase;
    private readonly HttpClient http = new();
    private Button? settingsButton;
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
    private DateTime crossfadeStarted;
    private bool isCrossfading;
    private Playlist? currentPlaylist;
    private Track? currentTrack;

    public PlaylistMainForm()
    {
        Core.Initialize();
        settings = LoadSettings();
        vlc = new LibVLC("--no-video", "--network-caching=1000");
        player = new MediaPlayer(vlc) { Volume = volume.Value };
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
        var header = new Label { Text = "▶  CatLu YNet 1.1.1", Dock = DockStyle.Fill, Padding = new Padding(5), Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(20, 20, 20), TextAlign = ContentAlignment.MiddleLeft };
        settingsButton = Button("⚙", Color.FromArgb(42, 42, 42)); settingsButton.Dock = DockStyle.Fill;
        updateBadge = new Label { Text = "1", Visible = false, AutoSize = false, Width = 18, Height = 18, Left = 27, Top = 3, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(229, 9, 20), ForeColor = Color.White, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        var settingsSlot = new Panel { Dock = DockStyle.Right, Width = 48, BackColor = surfaceColor }; settingsSlot.Controls.Add(settingsButton); settingsSlot.Controls.Add(updateBadge);
        var headerBar = new Panel { Dock = DockStyle.Fill, BackColor = surfaceColor }; headerBar.Controls.Add(header); headerBar.Controls.Add(settingsSlot);
        var stationBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(20, 20, 20), Padding = new Padding(5) };
        stationBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stationBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        stationBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var stationTitle = new Label { Text = "Станции", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
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

        Load += async (_, _) => { LoadLibrary(); await CheckForUpdateAsync(false); };
        FormClosing += (_, _) => { SaveLibrary(); Stop(); player.Dispose(); vlc.Dispose(); };
        Resize += (_, _) => RenderPlaylists();
        save.Click += async (_, _) => await SaveLinkAsync();
        create.Click += (_, _) => CreatePlaylist();
        previous.Click += async (_, _) => await PreviousAsync();
        play.Click += async (_, _) => await PlayCurrentAsync();
        pause.Click += (_, _) => player.Pause();
        stop.Click += (_, _) => { Stop(); status.Text = "Остановлено"; };
        next.Click += async (_, _) => await NextAsync();
        volume.Scroll += (_, _) => player.Volume = volume.Value;
        crossfadeTimer.Tick += async (_, _) => await HandleCrossfadeAsync();
        settingsButton.Click += (_, _) => ShowSettings();
        infoTimer.Tick += (_, _) => UpdateInfo();
        visualizer.Paint += (_, e) => DrawVisualizer(e.Graphics, visualizer.ClientRectangle);
        visualizerTimer.Tick += (_, _) => { if (visualizer.Visible && player.IsPlaying) { visualizerPhase++; visualizer.Invalidate(); } };
        infoTimer.Start();
        visualizerTimer.Start();
        ApplySettings();
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
                var playlist = new Playlist { Name = title, Expanded = true };
                playlist.Tracks.AddRange(await ReadPlaylistAsync(urlBox.Text));
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
            SaveLibrary();
            urlBox.Clear();
            status.Text = "Сохранено";
            RenderPlaylists();
        }
        catch (Exception ex) { status.Text = $"Ошибка: {Short(ex.Message)}"; }
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
        playlistHost.SuspendLayout();
        playlistHost.Controls.Clear();
        Panel? activeRow = null;
        var width = Math.Max(420, playlistHost.ClientSize.Width - 30);
        foreach (var playlist in playlists)
        {
            var rowHeights = playlist.Tracks.Select(track => TrackHeight(track, width)).ToList();
            var card = new Panel { Width = width, Height = playlist.Expanded ? 48 + rowHeights.Sum() : 48, BackColor = cardColor, Margin = new Padding(0, 0, 0, 5) };
            var headerRow = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = surfaceColor };
            var header = new Button { Text = $"{(playlist.Expanded ? "⌄" : "›")}   {playlist.Name}    {playlist.Tracks.Count}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = headerRow.BackColor, ForeColor = textColor, Font = new Font("Segoe UI", 11, FontStyle.Bold), Padding = new Padding(5, 0, 0, 0) };
            var editPlaylist = new Button { Text = "✎", Dock = DockStyle.Right, Width = 52, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = headerRow.BackColor, ForeColor = textColor, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
            header.Click += (_, _) => { playlist.Expanded = !playlist.Expanded; SaveLibrary(); RenderPlaylists(); };
            editPlaylist.Click += (_, _) => EditPlaylist(playlist);
            headerRow.Controls.Add(header);
            headerRow.Controls.Add(editPlaylist);
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
        if (focusCurrent && activeRow is not null) BeginInvoke(() => playlistHost.ScrollControlIntoView(activeRow));
    }

    private Panel AddTrackRow(Panel card, Playlist playlist, Track track, int top, int height)
    {
        var active = ReferenceEquals(playlist, currentPlaylist) && ReferenceEquals(track, currentTrack);
        var activeColor = Color.FromArgb((cardColor.R + accentColor.R) / 2, (cardColor.G + accentColor.G) / 2, (cardColor.B + accentColor.B) / 2);
        var row = new Panel { Left = 0, Top = top, Width = card.Width, Height = height, BackColor = active ? activeColor : cardColor, BorderStyle = active ? BorderStyle.FixedSingle : BorderStyle.None, Cursor = Cursors.Hand };
        var titleHeight = TextRenderer.MeasureText(track.Name, new Font("Segoe UI", 10, FontStyle.Bold), new Size(row.Width - 10, int.MaxValue), TextFormatFlags.WordBreak).Height;
        var title = new Label { Text = track.Name, Left = 5, Top = 5, Width = row.Width - 10, Height = titleHeight, ForeColor = textColor, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        var artist = new Label { Text = track.Artist, Left = 5, Top = 5 + titleHeight, Width = row.Width - 10, Height = 18, ForeColor = mutedColor };
        Label? source = null;
        if (settings.ShowYoutubeLinks) source = new Label { Text = $"https://www.youtube.com/watch?v={track.Id}", Left = 5, Top = artist.Bottom, Width = row.Width - 10, Height = 18, ForeColor = accentColor, Font = new Font("Segoe UI", 8, FontStyle.Underline), Cursor = Cursors.Hand };
        EventHandler play = async (_, _) => { currentPlaylist = playlist; currentTrack = track; RenderPlaylists(true); await PlayCurrentAsync(); };
        row.Click += play; title.Click += play; artist.Click += play;
        if (source is not null) source.Click += (_, _) => Process.Start(new ProcessStartInfo(source.Text) { UseShellExecute = true });
        row.Controls.Add(title); row.Controls.Add(artist); if (source is not null) row.Controls.Add(source); card.Controls.Add(row);
        return row;
    }

    private int TrackHeight(Track track, int width)
    {
        var measured = TextRenderer.MeasureText(track.Name, new Font("Segoe UI", 10, FontStyle.Bold), new Size(Math.Max(120, width - 10), int.MaxValue), TextFormatFlags.WordBreak);
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
        var remove = Button("Удалить", Color.FromArgb(229, 9, 20));
        var removePlaylist = Button("Удалить весь плейлист", Color.FromArgb(229, 9, 20));
        var close = Button("Готово", Color.FromArgb(55, 55, 55));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0) };
        actions.Controls.AddRange([up, down, remove, removePlaylist, close]);
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
        remove.Click += (_, _) => { if (tracks.SelectedItem is Track track) { playlist.Tracks.Remove(track); RefreshTracks(); SaveLibrary(); RenderPlaylists(); } };
        removePlaylist.Click += (_, _) =>
        {
            if (MessageBox.Show($"Удалить плейлист «{playlist.Name}»?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            playlists.Remove(playlist);
            SaveLibrary();
            RenderPlaylists();
            dialog.Close();
        };
        up.Click += (_, _) => MoveTrack(playlist, tracks, -1, RefreshTracks);
        down.Click += (_, _) => MoveTrack(playlist, tracks, 1, RefreshTracks);
        add.Click += async (_, _) =>
        {
            if (!IsYouTubeUrl(link.Text, out var isPlaylist) || isPlaylist) return;
            try
            {
                add.Enabled = false;
                var track = await ReadTrackAsync(link.Text);
                if (playlist.Tracks.All(item => item.Id != track.Id)) playlist.Tracks.Add(track);
                link.Clear(); RefreshTracks(); SaveLibrary(); RenderPlaylists();
            }
            catch (Exception ex) { MessageBox.Show(Short(ex.Message), "Ошибка добавления", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { add.Enabled = true; }
        };
        close.Click += (_, _) => dialog.Close();
        RefreshTracks();
        dialog.ShowDialog(this);
    }

    private static void MoveTrack(Playlist playlist, ListBox tracks, int offset, Action refresh)
    {
        if (tracks.SelectedItem is not Track track) return;
        var from = playlist.Tracks.IndexOf(track);
        var to = from + offset;
        if (to < 0 || to >= playlist.Tracks.Count) return;
        playlist.Tracks.RemoveAt(from);
        playlist.Tracks.Insert(to, track);
        refresh();
        tracks.SelectedItem = track;
    }

    private async Task PlayCurrentAsync()
    {
        if (currentTrack is null)
        {
            currentPlaylist = playlists.FirstOrDefault(playlist => playlist.Tracks.Count > 0);
            currentTrack = currentPlaylist?.Tracks.FirstOrDefault();
        }
        if (currentTrack is null) return;
        currentPlaylist!.Expanded = true;
        RenderPlaylists(true);
        crossfadeAttemptedTrack = null;
        try
        {
            status.Text = "Получение аудиопотока...";
            var output = await RunYtDlpAsync("--no-playlist", "-f", "bestaudio/best", "-g", $"https://www.youtube.com/watch?v={currentTrack.Id}");
            var url = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("yt-dlp не вернул поток.");
            Stop();
            media = new Media(vlc, new Uri(url));
            if (settings.NormalizeAudio) media.AddOption(":audio-filter=compressor");
            player.Volume = volume.Value;
            if (!player.Play(media)) throw new InvalidOperationException("LibVLC не открыл поток.");
            crossfadeTimer.Start();
        }
        catch (Exception ex) { status.Text = $"Ошибка: {Short(ex.Message)}"; }
    }

    private async Task NextAsync()
    {
        if (currentPlaylist is null || currentTrack is null) return;
        Stop();
        currentTrack = GetNextTrack();
        RenderPlaylists(true);
        await PlayCurrentAsync();
    }

    private async Task PreviousAsync()
    {
        if (currentPlaylist is null || currentTrack is null) return;
        Stop();
        var index = currentPlaylist.Tracks.IndexOf(currentTrack);
        currentTrack = currentPlaylist.Tracks[(index - 1 + currentPlaylist.Tracks.Count) % currentPlaylist.Tracks.Count];
        await PlayCurrentAsync();
    }

    private Track GetNextTrack()
    {
        var index = currentPlaylist!.Tracks.IndexOf(currentTrack!);
        return currentPlaylist.Tracks[(index + 1) % currentPlaylist.Tracks.Count];
    }

    private void AttachPlayerEvents(MediaPlayer target)
    {
        target.Playing += (_, _) => { if (ReferenceEquals(target, player)) BeginInvoke(() => status.Text = $"Играет: {currentTrack?.Name}"); };
        target.Paused += (_, _) => { if (ReferenceEquals(target, player)) BeginInvoke(() => status.Text = "Пауза"); };
        target.EncounteredError += (_, _) => { if (ReferenceEquals(target, player)) BeginInvoke(() => status.Text = "Ошибка аудиопотока"); };
        target.EndReached += (_, _) => { if (ReferenceEquals(target, player) && !isCrossfading) BeginInvoke(async () => await NextAsync()); };
    }

    private async Task HandleCrossfadeAsync()
    {
        if (isCrossfading)
        {
            if (incomingPlayer is null) return;
            var progress = Math.Min(1d, (DateTime.UtcNow - crossfadeStarted).TotalMilliseconds / 5000d);
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
        isCrossfading = true;
        crossfadeAttemptedTrack = currentTrack;
        try
        {
            var next = GetNextTrack();
            var output = await RunYtDlpAsync("--no-playlist", "-f", "bestaudio/best", "-g", $"https://www.youtube.com/watch?v={next.Id}");
            var url = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("yt-dlp не вернул следующий поток.");
            incomingMedia = new Media(vlc, new Uri(url));
            if (settings.NormalizeAudio) incomingMedia.AddOption(":audio-filter=compressor");
            incomingPlayer = new MediaPlayer(vlc) { Volume = 0 };
            AttachPlayerEvents(incomingPlayer);
            if (!incomingPlayer.Play(incomingMedia)) throw new InvalidOperationException("LibVLC не открыл следующий поток.");
            incomingTrack = next;
            crossfadeStarted = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            incomingPlayer?.Dispose(); incomingPlayer = null;
            incomingMedia?.Dispose(); incomingMedia = null;
            isCrossfading = false;
            status.Text = $"Кроссфейд: {Short(ex.Message)}";
        }
    }

    private void CompleteCrossfade()
    {
        var outgoingPlayer = player;
        outgoingPlayer.Stop();
        media?.Dispose();
        player = incomingPlayer!;
        media = incomingMedia;
        currentTrack = incomingTrack;
        currentPlaylist!.Expanded = true;
        incomingPlayer = null; incomingMedia = null; incomingTrack = null;
        player.Volume = volume.Value;
        isCrossfading = false;
        crossfadeAttemptedTrack = null;
        outgoingPlayer.Dispose();
        RenderPlaylists(true);
        status.Text = $"Играет: {currentTrack?.Name}";
    }

    private void Stop()
    {
        crossfadeTimer.Stop();
        isCrossfading = false;
        incomingPlayer?.Stop(); incomingPlayer?.Dispose(); incomingPlayer = null;
        incomingMedia?.Dispose(); incomingMedia = null; incomingTrack = null;
        player.Stop(); media?.Dispose(); media = null;
    }

    private async Task<Track> ReadTrackAsync(string url)
    {
        using var document = JsonDocument.Parse(await RunYtDlpAsync("--no-playlist", "--dump-single-json", CleanVideoUrl(url)));
        return ToTrack(document.RootElement);
    }

    private async Task<List<Track>> ReadPlaylistAsync(string url)
    {
        var output = await RunYtDlpAsync("--flat-playlist", "--dump-json", url);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { using var document = JsonDocument.Parse(line); return ToTrack(document.RootElement); }).Where(track => !string.IsNullOrWhiteSpace(track.Id)).ToList();
    }

    private static Track ToTrack(JsonElement value) => new() { Id = value.GetProperty("id").GetString() ?? "", Name = value.GetProperty("title").GetString() ?? "Без названия", Artist = value.TryGetProperty("uploader", out var author) ? author.GetString() ?? "YouTube" : "YouTube" };
    private static bool IsYouTubeUrl(string value, out bool playlist)
    {
        playlist = false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !(uri.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))) return false;
        playlist = GetVideoId(uri) is null && GetQueryValue(uri, "list") is not null;
        return true;
    }

    private static string CleanVideoUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        var id = GetVideoId(uri);
        return string.IsNullOrWhiteSpace(id) ? value : $"https://www.youtube.com/watch?v={Uri.EscapeDataString(id)}";
    }

    private static string? GetVideoId(Uri uri)
    {
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)) return uri.AbsolutePath.Trim('/');
        var queryId = GetQueryValue(uri, "v");
        if (!string.IsNullOrWhiteSpace(queryId)) return queryId;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        return parts.Length == 2 && (parts[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("embed", StringComparison.OrdinalIgnoreCase)) ? parts[1] : null;
    }

    private static string? GetQueryValue(Uri uri, string name) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=', 2)).FirstOrDefault(part => part.Length == 2 && part[0].Equals(name, StringComparison.OrdinalIgnoreCase)) is { } pair ? Uri.UnescapeDataString(pair[1]) : null;
    private static async Task<string> RunYtDlpAsync(params string[] arguments)
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
        using var process = Process.Start(start) ?? throw new InvalidOperationException("yt-dlp не запустился.");
        var output = await process.StandardOutput.ReadToEndAsync(); var error = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatLuYNet", "yt-dlp-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, error);
            throw new InvalidOperationException(error.Trim());
        }
        return output;
    }

    private async Task CheckForUpdateAsync(bool showResult)
    {
        if (checkingUpdate) return;
        checkingUpdate = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Maksimasz/CatLuYNet/releases/latest");
            request.Headers.UserAgent.ParseAdd("CatLuYNet/1.0");
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
            if (showResult) MessageBox.Show($"{T("updateError")}: {Short(ex.Message)}", T("updates"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            SaveSettings(); ApplySettings(); dialog.Close();
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
        visualizer.Visible = settings.MusicLights; visualizerRow!.Height = settings.MusicLights ? 42 : 0; bottomRow!.Height = settings.MusicLights ? 128 : 86; visualizer.Invalidate();
        RightToLeft = CurrentLanguage() == "he" ? RightToLeft.Yes : RightToLeft.No;
        foreach (var (control, key) in localizedControls) control.Text = T(key);
        urlBox.PlaceholderText = T("url"); RenderPlaylists(); UpdateInfo();
    }

    private void ApplyColors(Control control)
    {
        foreach (Control child in control.Controls)
        {
            if (child is TextBox or ComboBox or ListBox) { child.BackColor = cardColor; child.ForeColor = textColor; }
            else if (child is Button button) { button.BackColor = Equals(button.Tag, "accent") ? accentColor : cardColor; button.ForeColor = textColor; }
            else if (child is Label label) { label.ForeColor = Equals(label.Tag, "accent") ? accentColor : Equals(label.Tag, "muted") ? mutedColor : textColor; }
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
        if (settings.ShowTrackInfo && player.IsPlaying && currentTrack is not null) status.Text = $"{T("playing")}: {FormatTime(player.Time)} / {FormatTime(player.Length)} · {currentTrack.Artist}";
    }
    private static string FormatTime(long milliseconds) => milliseconds <= 0 ? "--:--" : TimeSpan.FromMilliseconds(milliseconds).ToString(@"m\:ss");
    private static bool SystemThemeIsDark() => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;

    private void DrawVisualizer(Graphics graphics, Rectangle area)
    {
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

    private AppSettings LoadSettings()
    {
        try { return File.Exists(settingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? new AppSettings() : new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
    }
    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
    }

    private void LoadLibrary()
    {
        try
        {
            var path = File.Exists(statePath) ? statePath : oldStatePath;
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            if (json.TrimStart().StartsWith('{')) playlists.AddRange(JsonSerializer.Deserialize<Library>(json)?.Playlists ?? []);
            else playlists.Add(new Playlist { Name = "Сохранённые", Expanded = true, Tracks = JsonSerializer.Deserialize<List<Track>>(json) ?? [] });
        }
        catch (JsonException) { }
        RenderPlaylists();
    }

    private void SaveLibrary()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(new Library { Playlists = playlists }));
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

    private sealed class Library { public List<Playlist> Playlists { get; set; } = []; }
    private sealed class AppSettings { public int Theme { get; set; } = 0; public int Language { get; set; } = 0; public bool ShowClock { get; set; } = false; public bool ShowTrackInfo { get; set; } = false; public bool NormalizeAudio { get; set; } = true; public bool Crossfade { get; set; } = true; public bool MusicLights { get; set; } = false; public bool ShowYoutubeLinks { get; set; } = false; }
    private sealed class Playlist { public string Name { get; set; } = ""; public bool Expanded { get; set; } = true; public List<Track> Tracks { get; set; } = []; }
    private sealed class Track { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Artist { get; set; } = ""; }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["ru"] = new() { ["save"] = "Сохранить", ["newPlaylist"] = "+ СОЗДАТЬ ПЛЕЙЛИСТ", ["stations"] = "Станции", ["volume"] = "Громкость", ["settings"] = "Настройки", ["theme"] = "Тема", ["language"] = "Язык", ["clock"] = "Показывать часы", ["trackInfo"] = "Информация о треке", ["normalize"] = "Выравнивание звука", ["crossfade"] = "Кроссфейд", ["musicLights"] = "Цветомузыка", ["youtubeLinks"] = "Показывать ссылки YouTube", ["checkUpdates"] = "Проверить обновления", ["updates"] = "Обновления", ["upToDate"] = "Установлена последняя версия.", ["updateError"] = "Не удалось проверить обновления", ["playing"] = "Играет", ["url"] = "Вставьте ссылку на ролик или плейлист YouTube" },
        ["en"] = new() { ["save"] = "Save", ["newPlaylist"] = "+ CREATE PLAYLIST", ["stations"] = "Stations", ["volume"] = "Volume", ["settings"] = "Settings", ["theme"] = "Theme", ["language"] = "Language", ["clock"] = "Show clock", ["trackInfo"] = "Track information", ["normalize"] = "Normalize audio", ["crossfade"] = "Crossfade", ["playing"] = "Playing", ["url"] = "Paste a YouTube video or playlist link" },
        ["he"] = new() { ["save"] = "שמור", ["newPlaylist"] = "+ פלייליסט חדש", ["stations"] = "תחנות", ["volume"] = "עוצמה", ["settings"] = "הגדרות", ["theme"] = "ערכת נושא", ["language"] = "שפה", ["clock"] = "הצג שעון", ["trackInfo"] = "פרטי רצועה", ["normalize"] = "איזון עוצמה", ["crossfade"] = "מעבר חלק", ["playing"] = "מנגן", ["url"] = "הדביקו קישור YouTube" },
        ["lt"] = new() { ["save"] = "Išsaugoti", ["newPlaylist"] = "+ NAUJAS GROJARAŠTIS", ["stations"] = "Stotys", ["volume"] = "Garsumas", ["settings"] = "Nustatymai", ["theme"] = "Tema", ["language"] = "Kalba", ["clock"] = "Rodyti laikrodį", ["trackInfo"] = "Takelio informacija", ["normalize"] = "Garso lyginimas", ["crossfade"] = "Kryžminis perėjimas", ["playing"] = "Groja", ["url"] = "Įklijuokite YouTube nuorodą" }
    };
}
