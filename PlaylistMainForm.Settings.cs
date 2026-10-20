using System.Globalization;
using System.Text.Json;
using CatLuYNet.Core;
using LibVLCSharp.Shared;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
    private void ShowSettings()
    {
        using var dialog = new Form { Text = T("settings"), Size = new Size(560, 640), StartPosition = FormStartPosition.CenterParent, BackColor = surfaceColor, ForeColor = textColor, Padding = new Padding(5), MinimizeBox = false, MaximizeBox = false };
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
        var import = Button(T("importPlaylists"), cardColor);
        var export = Button(T("exportPlaylists"), cardColor);
        foreach (var button in new[] { checkUpdates, save, import, export })
        {
            button.AutoSize = false;
            button.Padding = Padding.Empty;
            button.TextAlign = ContentAlignment.MiddleCenter;
        }
        foreach (var button in new[] { import, export }) { button.Dock = DockStyle.Fill; button.Enabled = libraryLoaded; }
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 15, Padding = new Padding(5), AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 15; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.Controls.Add(new Label { Text = T("theme"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); grid.Controls.Add(theme, 1, 0);
        grid.Controls.Add(new Label { Text = T("language"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1); grid.Controls.Add(language, 1, 1);
        grid.Controls.Add(showClock, 1, 2); grid.Controls.Add(showInfo, 1, 3); grid.Controls.Add(normalize, 1, 4); grid.Controls.Add(crossfade, 1, 5); grid.Controls.Add(musicLights, 1, 6); grid.Controls.Add(youtubeLinks, 1, 7); grid.Controls.Add(checkUpdates, 1, 8); grid.Controls.Add(save, 1, 14);
        grid.Controls.Add(import, 0, 9); grid.SetColumnSpan(import, 2);
        grid.Controls.Add(export, 0, 10); grid.SetColumnSpan(export, 2);
        var folder = new TextBox { Text = settings.DownloadFolder, Dock = DockStyle.Fill };
        var browse = Button("Папка MP3…", cardColor);
        browse.AutoSize = false; browse.Dock = DockStyle.Fill; browse.Padding = Padding.Empty; browse.TextAlign = ContentAlignment.MiddleCenter;
        browse.Click += (_, _) => { using var picker = new FolderBrowserDialog { SelectedPath = folder.Text }; if (picker.ShowDialog(dialog) == DialogResult.OK) folder.Text = picker.SelectedPath; };
        var quality = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        string[] qualities = ["0", "320K", "256K", "192K", "128K"];
        quality.Items.AddRange(["Наилучшее (VBR)", "320 кбит/с", "256 кбит/с", "192 кбит/с", "128 кбит/с"]);
        quality.SelectedIndex = Math.Max(0, Array.IndexOf(qualities, settings.Mp3Quality));
        grid.Controls.Add(browse, 0, 11); grid.Controls.Add(folder, 1, 11);
        grid.Controls.Add(new Label { Text = "Качество MP3", AutoSize = true }, 0, 12); grid.Controls.Add(quality, 1, 12);
        var folderHint = new Label { Text = "Внутри создаётся папка с названием плейлиста.", Dock = DockStyle.Fill, AutoEllipsis = true };
        grid.Controls.Add(folderHint, 0, 13); grid.SetColumnSpan(folderHint, 2);
        dialog.Controls.Add(grid);
        save.Click += (_, _) =>
        {
            try { settings.DownloadFolder = Path.GetFullPath(folder.Text.Trim()); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { MessageBox.Show(dialog, "Укажите корректную папку MP3."); return; }
            settings.Mp3Quality = qualities[quality.SelectedIndex];
            settings.Theme = theme.SelectedIndex; settings.Language = language.SelectedIndex; settings.ShowClock = showClock.Checked; settings.ShowTrackInfo = showInfo.Checked; settings.NormalizeAudio = normalize.Checked; settings.Crossfade = crossfade.Checked; settings.MusicLights = musicLights.Checked; settings.ShowYoutubeLinks = youtubeLinks.Checked;
            if (SaveSettings()) { ApplySettings(); dialog.Close(); }
        };
        checkUpdates.Click += async (_, _) => await CheckForUpdateAsync(true);
        import.Click += (_, _) => ImportPlaylists(dialog);
        export.Click += (_, _) => ExportPlaylists(dialog);
        dialog.ShowDialog(this);
    }

    private void ImportPlaylists(Form owner)
    {
        using var file = new OpenFileDialog { Title = T("importPlaylists"), Filter = "JSON (*.json)|*.json", CheckFileExists = true };
        if (file.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            var imported = PlaylistStore.Import(file.FileName, statePath, playlists);
            playlists.AddRange(imported);
            RenderPlaylists();
            MessageBox.Show(owner, $"{T("importedPlaylists")}: {imported.Count}", T("importPlaylists"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, $"{T("importError")}: {Short(ex.Message)}", T("importPlaylists"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportPlaylists(Form owner)
    {
        var selected = SelectPlaylistForExport(owner);
        if (selected is null) return;
        using var file = new SaveFileDialog { Title = T("exportPlaylists"), Filter = "JSON (*.json)|*.json", DefaultExt = "json", FileName = PlaylistFileName(selected.Name), AddExtension = true, OverwritePrompt = true };
        if (file.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            PlaylistStore.Save(file.FileName, [selected]);
            MessageBox.Show(owner, T("exportedPlaylists"), T("exportPlaylists"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, $"{T("exportError")}: {Short(ex.Message)}", T("exportPlaylists"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string PlaylistFileName(string name)
    {
        var safeName = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (safeName.Length == 0) safeName = "Playlist";
        if (System.Text.RegularExpressions.Regex.IsMatch(safeName, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) safeName = "_" + safeName;
        return safeName + ".json";
    }

    private Playlist? SelectPlaylistForExport(Form owner)
    {
        using var dialog = new Form { Text = T("exportPlaylists"), Size = new Size(430, 420), StartPosition = FormStartPosition.CenterParent, BackColor = surfaceColor, ForeColor = textColor, MinimizeBox = false, MaximizeBox = false, Padding = new Padding(8) };
        var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = nameof(Playlist.Name), SelectionMode = SelectionMode.One, BackColor = cardColor, ForeColor = textColor, BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = true };
        list.Items.AddRange(playlists.ToArray());
        var export = Button(T("exportPlaylists"), accentColor);
        export.Dock = DockStyle.Bottom;
        export.AutoSize = false;
        export.Height = 38;
        export.Padding = Padding.Empty;
        export.Enabled = false;
        export.DialogResult = DialogResult.OK;
        list.SelectedIndexChanged += (_, _) => export.Enabled = list.SelectedItem is Playlist;
        dialog.Controls.Add(list);
        dialog.Controls.Add(export);
        dialog.AcceptButton = export;
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        return dialog.ShowDialog(owner) == DialogResult.OK ? list.SelectedItem as Playlist : null;
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
        updateLink!.LinkColor = updateLink.ActiveLinkColor = updateLink.VisitedLinkColor = accentColor;
        if (updateVersion is not null) SetUpdateLinkText($"{T("updateAvailable")} {updateVersion}");
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
            ApplyControlColor(child);
            ApplyColors(child);
        }
    }

    private void ApplyControlColor(Control control)
    {
        if (Equals(control.Tag, "header") || Equals(control.Tag, "application-icon"))
        {
            control.BackColor = surfaceColor;
            control.ForeColor = textColor;
        }
        else if (control is TextBox or ComboBox or ListBox)
        {
            control.BackColor = cardColor;
            control.ForeColor = textColor;
        }
        else if (control is Button button)
        {
            button.BackColor = Equals(button.Tag, "header-action") ? surfaceColor : Equals(button.Tag, "accent") ? accentColor : cardColor;
            button.ForeColor = textColor;
        }
        else if (control is Label label) ApplyLabelColor(label);
        else if (control is Panel or TableLayoutPanel or FlowLayoutPanel) control.BackColor = surfaceColor;
    }

    private void ApplyLabelColor(Label label)
    {
        if (Equals(label.Tag, "accent") || Equals(label.Tag, "muted") || Equals(label.Tag, "header")) label.BackColor = surfaceColor;
        label.ForeColor = Equals(label.Tag, "accent") ? accentColor : Equals(label.Tag, "muted") ? mutedColor : textColor;
    }

    private void RegisterLocalized(Control control, string key) => localizedControls[control] = key;
    private string CurrentLanguage() => settings.Language switch { 1 => "ru", 2 => "en", 3 => "he", 4 => "lt", _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName };
    private string T(string key) => Translations.TryGetValue(CurrentLanguage(), out var language) && language.TryGetValue(key, out var text) ? text : Translations["ru"][key];
    private void UpdateInfo()
    {
        clock.Text = DateTime.Now.ToString("HH:mm");
        if (settings.ShowTrackInfo && player.IsPlaying && currentTrack is not null) status.Text = $"{T("playing")}: {FormatTime(player.Time)} / {FormatTime(player.Length)} · {currentTrack.Name} · {PlaybackSource()}";
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

    private void PlayDancingCat()
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
        ClearTrackArtwork();
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
                if (!closing && (settings.MusicLights || miniPlayerForm is not null) && catMedia is not null && trackArtwork.Image is null) catPlayer.Play(catMedia);
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

    private sealed class AppSettings { public string DownloadFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CatLu YNet"); public string Mp3Quality { get; set; } = "0"; public int Theme { get; set; } = 0; public int Language { get; set; } = 0; public bool ShowClock { get; set; } = false; public bool ShowTrackInfo { get; set; } = false; public bool NormalizeAudio { get; set; } = true; public bool Crossfade { get; set; } = true; public bool MusicLights { get; set; } = false; public bool ShowYoutubeLinks { get; set; } = false; }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["ru"] = new() { ["importPlaylists"] = "Импорт плейлистов", ["exportPlaylists"] = "Экспорт плейлистов", ["importedPlaylists"] = "Добавлено плейлистов", ["exportedPlaylists"] = "Плейлисты экспортированы.", ["importError"] = "Не удалось импортировать плейлисты", ["exportError"] = "Не удалось экспортировать плейлисты", ["save"] = "Сохранить", ["newPlaylist"] = "+ СОЗДАТЬ ПЛЕЙЛИСТ", ["stations"] = "Плейлисты", ["newTrack"] = "НОВОЕ", ["linkPlaylist"] = "Связать YouTube", ["volume"] = "Громкость", ["settings"] = "Настройки", ["theme"] = "Тема", ["language"] = "Язык", ["clock"] = "Показывать часы", ["trackInfo"] = "Информация о треке", ["normalize"] = "Выравнивание звука", ["crossfade"] = "Кроссфейд", ["musicLights"] = "Цветомузыка", ["youtubeLinks"] = "Показывать ссылки YouTube", ["checkUpdates"] = "Проверить обновления", ["updates"] = "Обновления", ["updateAvailable"] = "Доступно обновление", ["downloadingUpdate"] = "Скачивание обновления...", ["upToDate"] = "Установлена последняя версия.", ["updateError"] = "Не удалось обновить программу", ["playing"] = "Играет", ["url"] = "Вставьте ссылку на ролик или плейлист YouTube" },
        ["en"] = new() { ["importPlaylists"] = "Import playlists", ["exportPlaylists"] = "Export playlists", ["importedPlaylists"] = "Playlists added", ["exportedPlaylists"] = "Playlists exported.", ["importError"] = "Could not import playlists", ["exportError"] = "Could not export playlists", ["save"] = "Save", ["newPlaylist"] = "+ CREATE PLAYLIST", ["stations"] = "Playlists", ["newTrack"] = "NEW", ["linkPlaylist"] = "Link YouTube", ["volume"] = "Volume", ["settings"] = "Settings", ["theme"] = "Theme", ["language"] = "Language", ["clock"] = "Show clock", ["trackInfo"] = "Track information", ["normalize"] = "Normalize audio", ["crossfade"] = "Crossfade", ["updateAvailable"] = "Update available", ["downloadingUpdate"] = "Downloading update...", ["upToDate"] = "You have the latest version.", ["updateError"] = "Update failed", ["playing"] = "Playing", ["url"] = "Paste a YouTube video or playlist link" },
        ["he"] = new() { ["importPlaylists"] = "ייבוא פלייליסטים", ["exportPlaylists"] = "ייצוא פלייליסטים", ["importedPlaylists"] = "פלייליסטים שנוספו", ["exportedPlaylists"] = "הפלייליסטים יוצאו.", ["importError"] = "ייבוא הפלייליסטים נכשל", ["exportError"] = "ייצוא הפלייליסטים נכשל", ["save"] = "שמור", ["newPlaylist"] = "+ פלייליסט חדש", ["stations"] = "פלייליסטים", ["newTrack"] = "חדש", ["linkPlaylist"] = "קישור YouTube", ["volume"] = "עוצמה", ["settings"] = "הגדרות", ["theme"] = "ערכת נושא", ["language"] = "שפה", ["clock"] = "הצג שעון", ["trackInfo"] = "פרטי רצועה", ["normalize"] = "איזון עוצמה", ["crossfade"] = "מעבר חלק", ["updateAvailable"] = "עדכון זמין", ["downloadingUpdate"] = "העדכון יורד...", ["upToDate"] = "מותקנת הגרסה העדכנית.", ["updateError"] = "העדכון נכשל", ["playing"] = "מנגן", ["url"] = "הדביקו קישור YouTube" },
        ["lt"] = new() { ["importPlaylists"] = "Importuoti grojaraščius", ["exportPlaylists"] = "Eksportuoti grojaraščius", ["importedPlaylists"] = "Pridėta grojaraščių", ["exportedPlaylists"] = "Grojaraščiai eksportuoti.", ["importError"] = "Nepavyko importuoti grojaraščių", ["exportError"] = "Nepavyko eksportuoti grojaraščių", ["save"] = "Išsaugoti", ["newPlaylist"] = "+ NAUJAS GROJARAŠTIS", ["stations"] = "Grojaraščiai", ["newTrack"] = "NAUJA", ["linkPlaylist"] = "Susieti YouTube", ["volume"] = "Garsumas", ["settings"] = "Nustatymai", ["theme"] = "Tema", ["language"] = "Kalba", ["clock"] = "Rodyti laikrodį", ["trackInfo"] = "Takelio informacija", ["normalize"] = "Garso lyginimas", ["crossfade"] = "Kryžminis perėjimas", ["updateAvailable"] = "Galimas naujinimas", ["downloadingUpdate"] = "Atsisiunčiamas naujinimas...", ["upToDate"] = "Naudojate naujausią versiją.", ["updateError"] = "Nepavyko atnaujinti", ["playing"] = "Groja", ["url"] = "Įklijuokite YouTube nuorodą" }
    };
}
