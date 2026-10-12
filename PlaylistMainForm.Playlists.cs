using System.Diagnostics;
using CatLuYNet.Core;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
    private async Task SaveLinkAsync()
    {
        if (!IsYouTubeUrl(urlBox.Text, out var isPlaylist)) { status.Text = "Введите ссылку YouTube"; return; }
        try
        {
            status.Text = "Чтение YouTube...";
            if (isPlaylist ? !await AddLinkedPlaylistAsync() : !await AddVideoToPlaylistAsync()) return;
            if (closing || !SaveLibrary()) return;
            urlBox.Clear();
            status.Text = "Сохранено";
            RenderPlaylists();
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { if (!closing) status.Text = $"Ошибка: {Short(ex.Message)}"; }
    }

    private async Task<bool> AddLinkedPlaylistAsync()
    {
        var title = Ask("Сохранить плейлист", "Название плейлиста:");
        if (string.IsNullOrWhiteSpace(title)) return false;
        var sourceUrl = urlBox.Text.Trim();
        var playlist = new Playlist { Name = title, Expanded = true, SourceUrl = sourceUrl, LastCheckedUtc = DateTime.UtcNow };
        playlist.Tracks.AddRange(await ReadPlaylistAsync(sourceUrl));
        playlists.Add(playlist);
        return true;
    }

    private async Task<bool> AddVideoToPlaylistAsync()
    {
        var playlist = SelectPlaylist();
        if (playlist is null) return false;
        var track = await ReadTrackAsync(urlBox.Text);
        if (playlist.Tracks.All(item => item.Id != track.Id)) playlist.Tracks.Add(track);
        playlist.Expanded = true;
        return true;
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
            playlistHost.Controls.Add(CreatePlaylistCard(playlist, width, ref activeRow));
        }
        playlistHost.ResumeLayout();
        if (focusCurrent && activeRow is not null && IsHandleCreated) BeginInvoke(() => { if (!closing && !activeRow.IsDisposed) playlistHost.ScrollControlIntoView(activeRow); });
    }

    private Panel CreatePlaylistCard(Playlist playlist, int width, ref Panel? activeRow)
    {
        var rowHeights = playlist.Tracks.Select(track => TrackHeight(track, width)).ToList();
        var headerRow = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = surfaceColor };
        var header = new Button { Text = $"{(playlist.Expanded ? "⌄" : "›")}   {playlist.Name}    {playlist.Tracks.Count}", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, BackColor = headerRow.BackColor, ForeColor = textColor, Font = playlistFont, Padding = new Padding(5, 0, 0, 0) };
        var actions = CreatePlaylistActions(playlist, headerRow.BackColor);
        header.Click += (_, _) => { playlist.Expanded = !playlist.Expanded; SaveLibrary(); RenderPlaylists(); };
        headerRow.Controls.Add(header);
        headerRow.Controls.Add(actions);
        var card = new Panel { Width = width, Height = playlist.Expanded ? 48 + rowHeights.Sum() : 48, BackColor = cardColor, Margin = new Padding(0, 0, 0, 5) };
        card.Controls.Add(headerRow);
        if (playlist.Expanded) AddPlaylistTracks(card, playlist, rowHeights, ref activeRow);
        return card;
    }

    private TableLayoutPanel CreatePlaylistActions(Playlist playlist, Color backColor)
    {
        var actions = new TableLayoutPanel { Dock = DockStyle.Right, Width = 76, ColumnCount = 2, RowCount = 1, BackColor = backColor };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        var edit = Button("✎", Color.Transparent);
        var delete = Button("×", Color.Transparent);
        foreach (var icon in new[] { edit, delete })
        {
            icon.AutoSize = false; icon.Dock = DockStyle.Fill; icon.Margin = new Padding(4); icon.Padding = Padding.Empty;
            icon.Font = editFont; icon.BackColor = Color.Transparent;
            icon.FlatAppearance.MouseOverBackColor = Color.Transparent;
            icon.FlatAppearance.MouseDownBackColor = Color.Transparent;
        }
        edit.ForeColor = mutedColor;
        delete.ForeColor = accentColor;
        edit.Click += (_, _) => EditPlaylist(playlist);
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show($"Удалить плейлист «{playlist.Name}»?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                DeletePlaylist(playlist);
        };
        actions.Controls.Add(edit, 0, 0); actions.Controls.Add(delete, 1, 0);
        return actions;
    }

    private void AddPlaylistTracks(Panel card, Playlist playlist, List<int> rowHeights, ref Panel? activeRow)
    {
        var top = 48;
        for (var i = 0; i < playlist.Tracks.Count; i++)
        {
            var track = playlist.Tracks[i];
            var row = AddTrackRow(card, playlist, track, top, rowHeights[i]);
            if (ReferenceEquals(playlist, currentPlaylist) && ReferenceEquals(track, currentTrack)) activeRow = row;
            top += rowHeights[i];
        }
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
        using var dialog = new PlaylistEditorDialog(this, playlist);
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

}
