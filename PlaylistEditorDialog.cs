using CatLuYNet.Core;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
    private sealed class PlaylistEditorDialog : Form
    {
        private readonly PlaylistMainForm owner;
        private readonly Playlist playlist;
        private readonly TextBox name;
        private readonly ListBox tracks;
        private readonly TextBox link;
        private readonly CheckBox download = new() { Text = "Скачать MP3", AutoSize = true };

        public PlaylistEditorDialog(PlaylistMainForm owner, Playlist playlist)
        {
            this.owner = owner;
            this.playlist = playlist;
            Text = $"Плейлист: {playlist.Name}";
            Size = new Size(620, 620);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(25, 25, 25);
            ForeColor = Color.White;
            Padding = new Padding(5);
            MinimizeBox = false;
            MaximizeBox = false;

            name = new TextBox { Text = playlist.Name, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11) };
            tracks = new ListBox { Dock = DockStyle.Fill, Margin = Padding.Empty, DrawMode = DrawMode.OwnerDrawVariable, BackColor = Color.FromArgb(35, 35, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), IntegralHeight = false, HorizontalScrollbar = false };
            link = new TextBox { Dock = DockStyle.Fill, Margin = Padding.Empty, PlaceholderText = "Ссылка YouTube для добавления", BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            BuildLayout();
            ConfigureTrackList();
            RefreshTracks();
        }

        private void BuildLayout()
        {
            var rename = Button("Переименовать", Color.FromArgb(55, 55, 55));
            var add = Button("Добавить", Color.FromArgb(229, 9, 20));
            var up = Button("↑", Color.FromArgb(55, 55, 55));
            var down = Button("↓", Color.FromArgb(55, 55, 55));
            var linkPlaylist = Button(owner.T("linkPlaylist"), Color.FromArgb(55, 55, 55));
            var remove = Button("Удалить", Color.FromArgb(229, 9, 20));
            var removePlaylist = Button("Удалить весь плейлист", Color.FromArgb(229, 9, 20));
            var close = Button("Готово", Color.FromArgb(55, 55, 55));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0) };
            var downloadOne = Button("Скачать выбранный MP3", Color.FromArgb(55, 55, 55));
            var downloadAll = Button("Скачать все MP3", Color.FromArgb(55, 55, 55));
            downloadOne.Click += async (_, _) => { if (tracks.SelectedItem is Track track) { await owner.DownloadTracksAsync(playlist, [track]); tracks.Invalidate(); } };
            downloadAll.Click += async (_, _) => { await owner.DownloadTracksAsync(playlist, playlist.Tracks.ToList()); tracks.Invalidate(); };
            foreach (var button in new[] { up, down, linkPlaylist, remove, removePlaylist, downloadOne, downloadAll, close })
            {
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.Padding = Padding.Empty;
                button.AutoSize = false;
                button.Size = new Size(Math.Max(38, TextRenderer.MeasureText(button.Text, button.Font).Width + 24), 36);
            }
            actions.Controls.AddRange([up, down, linkPlaylist, remove, removePlaylist, downloadOne, downloadAll, close]);

            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = Padding.Empty };
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            var nameRow = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, Padding = new Padding(0, 0, 0, 5) };
            nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            nameRow.Controls.Add(name, 0, 0);
            nameRow.Controls.Add(rename, 1, 0);
            var linkRow = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, Padding = new Padding(0, 0, 0, 5) };
            linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linkRow.Controls.Add(link, 0, 0);
            linkRow.ColumnCount = 3;
            linkRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            linkRow.Controls.Add(download, 1, 0);
            linkRow.Controls.Add(add, 2, 0);
            rename.Margin = new Padding(5, 0, 0, 0);
            add.Margin = new Padding(5, 0, 0, 0);
            editor.Controls.Add(nameRow, 0, 0);
            editor.Controls.Add(linkRow, 0, 1);
            editor.Controls.Add(tracks, 0, 2);
            editor.Controls.Add(actions, 0, 3);
            Controls.Add(editor);

            rename.Click += (_, _) => RenamePlaylist();
            remove.Click += (_, _) => RemoveSelectedTrack();
            removePlaylist.Click += (_, _) => DeletePlaylist();
            up.Click += (_, _) => owner.MoveTrack(playlist, tracks, -1, RefreshTracks);
            down.Click += (_, _) => owner.MoveTrack(playlist, tracks, 1, RefreshTracks);
            linkPlaylist.Click += async (_, _) => await LinkPlaylistAsync(linkPlaylist);
            add.Click += async (_, _) => await AddTrackAsync(add);
            close.Click += (_, _) => Close();
        }

        private void RefreshTracks()
        {
            tracks.DataSource = null;
            tracks.DataSource = playlist.Tracks;
            tracks.DisplayMember = nameof(Track.Name);
        }

        private void ConfigureTrackList()
        {
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
                using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(75, 15, 20) : Color.FromArgb(42, 42, 42));
                e.Graphics.FillRectangle(background, e.Bounds);
                var number = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 8, 30, e.Bounds.Height - 16);
                var title = new Rectangle(e.Bounds.X + 42, e.Bounds.Y + 7, e.Bounds.Width - 52, e.Bounds.Height - 28);
                TextRenderer.DrawText(e.Graphics, $"{e.Index + 1}", tracks.Font, number, Color.FromArgb(180, 180, 180), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(e.Graphics, track.Name, tracks.Font, title, Color.White, TextFormatFlags.WordBreak);
                TextRenderer.DrawText(e.Graphics, $"{track.Artist} · {owner.AudioSource(track)}", owner.Font, new Rectangle(e.Bounds.X + 42, e.Bounds.Bottom - 23, e.Bounds.Width - 52, 18), Color.Silver, TextFormatFlags.EndEllipsis);
            };
        }

        private void RenamePlaylist()
        {
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            playlist.Name = name.Text.Trim();
            Text = $"Плейлист: {playlist.Name}";
            owner.SaveLibrary();
            owner.RenderPlaylists();
        }

        private void RemoveSelectedTrack()
        {
            if (tracks.SelectedItem is not Track track) return;
            owner.RemoveTrack(playlist, track);
            RefreshTracks();
            owner.SaveLibrary();
            owner.RenderPlaylists();
        }

        private void DeletePlaylist()
        {
            if (MessageBox.Show($"Удалить плейлист «{playlist.Name}»?", owner.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            owner.DeletePlaylist(playlist);
            Close();
        }

        private async Task LinkPlaylistAsync(Button button)
        {
            var sourceUrl = owner.Ask("Связать с YouTube-плейлистом", "Ссылка на плейлист YouTube:");
            if (string.IsNullOrWhiteSpace(sourceUrl)) return;
            if (!IsYouTubeUrl(sourceUrl, out var isPlaylist) || !isPlaylist)
            {
                MessageBox.Show("Введите ссылку на плейлист YouTube.", "Неверная ссылка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                button.Enabled = false;
                var tracksFromSource = await owner.ReadPlaylistAsync(sourceUrl.Trim());
                if (owner.closing || IsDisposed) return;
                playlist.SourceUrl = sourceUrl.Trim();
                var added = PlaylistStore.PrependMissingTracks(playlist, tracksFromSource);
                playlist.LastCheckedUtc = DateTime.UtcNow;
                if (!owner.SaveLibrary()) return;
                RefreshTracks();
                owner.RenderPlaylists();
                owner.status.Text = $"Новых песен добавлено в начало: {added}";
            }
            catch (OperationCanceledException) when (owner.closing) { }
            catch (Exception ex) { if (!owner.closing && !IsDisposed) MessageBox.Show(Short(ex.Message), "Ошибка проверки плейлиста", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (!button.IsDisposed) button.Enabled = true; }
        }

        private async Task AddTrackAsync(Button button)
        {
            if (!IsYouTubeUrl(link.Text, out var isPlaylist) || isPlaylist) return;
            try
            {
                button.Enabled = false;
                var track = await owner.ReadTrackAsync(link.Text);
                if (owner.closing || IsDisposed || !owner.playlists.Contains(playlist)) return;
                if (playlist.Tracks.All(item => item.Id != track.Id)) playlist.Tracks.Add(track);
                link.Clear();
                RefreshTracks();
                if (owner.SaveLibrary() && download.Checked) await owner.DownloadTracksAsync(playlist, [playlist.Tracks.First(t => t.Id == track.Id)]);
                owner.RenderPlaylists();
            }
            catch (OperationCanceledException) when (owner.closing) { }
            catch (Exception ex) { if (!owner.closing && !IsDisposed) MessageBox.Show(Short(ex.Message), "Ошибка добавления", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (!button.IsDisposed) button.Enabled = true; }
        }

        private static bool IsYouTubeUrl(string value, out bool playlist) => YouTubeUrl.IsValid(value, out playlist);
        private static string Short(string value) { var clean = value.Replace(Environment.NewLine, " "); return clean[..Math.Min(clean.Length, 110)]; }
    }
}
