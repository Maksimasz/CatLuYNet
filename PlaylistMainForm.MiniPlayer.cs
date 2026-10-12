namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
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

}
