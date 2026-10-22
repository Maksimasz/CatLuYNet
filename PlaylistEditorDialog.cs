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
        private readonly CheckBox download = new() { Text = S("downloadMp3"), AutoSize = true };

        public PlaylistEditorDialog(PlaylistMainForm owner, Playlist playlist)
        {
            this.owner = owner;
            this.playlist = playlist;
            Text = S("playlist", playlist.Name);
            var workingArea = Screen.FromControl(owner).WorkingArea;
            MaximumSize = workingArea.Size;
            MinimumSize = new Size(Math.Min(500, workingArea.Width), Math.Min(420, workingArea.Height));
            Size = new Size(Math.Min(620, workingArea.Width), Math.Min(620, workingArea.Height));
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(25, 25, 25);
            ForeColor = Color.White;
            Padding = new Padding(5);
            MinimizeBox = false;
            MaximizeBox = false;

            name = new TextBox { Text = playlist.Name, Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11) };
            tracks = new ListBox { Dock = DockStyle.Fill, Margin = Padding.Empty, DrawMode = DrawMode.OwnerDrawVariable, BackColor = Color.FromArgb(35, 35, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), IntegralHeight = false, HorizontalScrollbar = false };
            link = new TextBox { Dock = DockStyle.Fill, Margin = Padding.Empty, PlaceholderText = S("youtubeLinkToAdd"), BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            BuildLayout();
            link.RightToLeft = RightToLeft.No;
            ConfigureTrackList();
            RefreshTracks();
            LocalizeDialog(this);
        }

        private void BuildLayout()
        {
            var rename = Button(S("rename"), Color.FromArgb(55, 55, 55));
            var add = Button(S("add"), Color.FromArgb(229, 9, 20));
            var up = Button("↑", Color.FromArgb(55, 55, 55));
            var down = Button("↓", Color.FromArgb(55, 55, 55));
            var linkPlaylist = Button(owner.T("linkPlaylist"), Color.FromArgb(55, 55, 55));
            var remove = Button(S("delete"), Color.FromArgb(229, 9, 20));
            var removePlaylist = Button(S("deleteEntirePlaylist"), Color.FromArgb(229, 9, 20));
            var close = Button(S("finishEditingPlaylist"), Color.FromArgb(55, 55, 55));
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 3; i++) actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var downloadOne = Button(S("downloadSelectedMp3"), Color.FromArgb(55, 55, 55));
            var downloadAll = Button(S("downloadAllMp3"), Color.FromArgb(55, 55, 55));
            downloadOne.Click += async (_, _) => { if (tracks.SelectedItem is Track track) { await owner.DownloadTracksAsync(playlist, [track]); tracks.Invalidate(); } };
            downloadAll.Click += async (_, _) => { await owner.DownloadTracksAsync(playlist, playlist.Tracks.ToList()); tracks.Invalidate(); };
            var deleteMp3 = Button(S("deleteLocalMp3"), Color.FromArgb(229, 9, 20));
            deleteMp3.Click += (_, _) => DeleteSelectedMp3();
            foreach (var button in new[] { up, down, linkPlaylist, remove, removePlaylist, downloadOne, downloadAll, deleteMp3, close })
            {
                button.TextAlign = ContentAlignment.MiddleCenter;
                button.Padding = Padding.Empty;
                button.AutoSize = false;
                button.Dock = DockStyle.Fill;
                button.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                button.Margin = new Padding(3);
                button.Resize += (_, _) => FitActionButton(button);
            }
            var playlistActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
            foreach (var width in new[] { 6f, 6f, 28f, 20f, 40f }) playlistActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            playlistActions.Controls.Add(up, 0, 0); playlistActions.Controls.Add(down, 1, 0);
            playlistActions.Controls.Add(linkPlaylist, 2, 0); playlistActions.Controls.Add(remove, 3, 0); playlistActions.Controls.Add(removePlaylist, 4, 0);
            var mp3Actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            foreach (var width in new[] { 40f, 27f, 33f }) mp3Actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            mp3Actions.Controls.Add(downloadOne, 0, 0); mp3Actions.Controls.Add(downloadAll, 1, 0); mp3Actions.Controls.Add(deleteMp3, 2, 0);
            actions.Controls.Add(playlistActions, 0, 0); actions.Controls.Add(mp3Actions, 0, 1); actions.Controls.Add(close, 0, 2);

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

        private static void FitActionButton(Button button)
        {
            for (var size = 8.5f; size >= 6f; size -= 0.5f)
            {
                using var font = new Font("Segoe UI", size, FontStyle.Bold);
                var text = TextRenderer.MeasureText(button.Text, font);
                if (size > 6f && (text.Width > button.ClientSize.Width - 8 || text.Height > button.ClientSize.Height - 4)) continue;
                if (button.Font.Size != size)
                {
                    var previous = button.Font;
                    button.Font = (Font)font.Clone();
                    previous.Dispose();
                }
                break;
            }
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

        private void DeleteSelectedMp3()
        {
            if (tracks.SelectedItem is not Track track) return;
            var path = owner.LocalAudio(track);
            if (path is null) { MessageBox.Show(this, S("localMp3NotFound"), S("deleteMp3")); return; }
            if (MessageBox.Show(this, S("confirmDeleteMp3", path), S("deleteMp3"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try { owner.DeleteLocalAudio(track); tracks.Invalidate(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, ex.Message, S("mp3DeleteError"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RenamePlaylist()
        {
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            playlist.Name = name.Text.Trim();
            Text = S("playlist", playlist.Name);
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
            if (MessageBox.Show(S("deletePlaylist", playlist.Name), owner.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            owner.DeletePlaylist(playlist);
            Close();
        }

        private async Task LinkPlaylistAsync(Button button)
        {
            var sourceUrl = owner.Ask(S("linkToAYoutubePlaylist"), S("youtubePlaylistLink"));
            if (string.IsNullOrWhiteSpace(sourceUrl)) return;
            if (!IsYouTubeUrl(sourceUrl, out var isPlaylist) || !isPlaylist)
            {
                MessageBox.Show(S("enterAYoutubePlaylistLink"), S("invalidLink"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                owner.SetStatus("newSongsAddedAtTheBeginning", added);
            }
            catch (OperationCanceledException) when (owner.closing) { }
            catch (Exception ex) { if (!owner.closing && !IsDisposed) MessageBox.Show(Short(ex.Message), S("playlistCheckError"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
                if (!owner.TryAddTrack(playlist, track, this)) return;
                link.Clear();
                RefreshTracks();
                if (owner.SaveLibrary() && download.Checked) await owner.DownloadTracksAsync(playlist, [playlist.Tracks.First(t => t.Id == track.Id)]);
                owner.RenderPlaylists();
            }
            catch (OperationCanceledException) when (owner.closing) { }
            catch (Exception ex) { if (!owner.closing && !IsDisposed) MessageBox.Show(Short(ex.Message), S("couldNotAddTrack"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { if (!button.IsDisposed) button.Enabled = true; }
        }

        private static bool IsYouTubeUrl(string value, out bool playlist) => YouTubeUrl.IsValid(value, out playlist);
        private static string Short(string value) { var clean = TranslateCoreError(value).Replace(Environment.NewLine, " "); return clean[..Math.Min(clean.Length, 110)]; }
    }
}
