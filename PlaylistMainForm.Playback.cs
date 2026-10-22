using System.Diagnostics;
using CatLuYNet.Core;
using LibVLCSharp.Shared;

namespace YouTubeRadio;

public sealed partial class PlaylistMainForm
{
    private async Task PlayCurrentAsync()
    {
        if (closing) return;
        if (paused)
        {
            ResumePlayback();
            return;
        }
        if (player.IsPlaying) return;
        if (!SelectCurrentTrack()) return;
        var track = currentTrack!;
        UpdateMiniPlayer();
        Stop();
        var token = playbackRequest.Token;
        currentPlaylist!.Expanded = true;
        RenderPlaylists(true);
        crossfadeAttemptedTrack = null;
        await LoadAndPlayCurrentAsync(track, token);
    }

    private void ResumePlayback()
    {
        paused = false;
        player.Mute = false;
        if (incomingPlayer is not null) incomingPlayer.Mute = false;
        player.SetPause(false);
        incomingPlayer?.SetPause(false);
        if (incomingPlayer is not null) crossfadeElapsed.Start();
        crossfadeTimer.Start();
        UpdateMiniPlayer();
    }

    private bool SelectCurrentTrack()
    {
        if (currentTrack is null)
        {
            currentPlaylist = playlists.FirstOrDefault(playlist => playlist.Tracks.Count > 0);
            currentTrack = currentPlaylist?.Tracks.FirstOrDefault();
        }
        return currentTrack is not null;
    }

    private async Task LoadAndPlayCurrentAsync(Track track, CancellationToken token)
    {
        try
        {
            SetStatus("loadingAudioStream");
            media = new Media(vlc, await ResolveAudioAsync(track, token));
            if (settings.NormalizeAudio) media.AddOption(":audio-filter=compressor");
            SetPlaybackVolume(volume.Value);
            if (!player.Play(media)) throw new InvalidOperationException(S("libvlcCouldNotOpenTheStream"));
            crossfadeTimer.Start();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!closing && !token.IsCancellationRequested) SetStatus("error", Short(ex.Message)); }
    }

    private void PausePlayback()
    {
        if (paused) return;
        if (media is null) { Stop(); SetStatus("paused"); return; }
        paused = true;
        player.SetPause(true);
        incomingPlayer?.SetPause(true);
        crossfadeElapsed.Stop();
        crossfadeTimer.Stop();
        SetStatus("paused");
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
            if (paused) SetStatus("paused"); else SetStatus("playingTrack", currentTrack?.Name, PlaybackSource());
            UpdateMiniPlayer();
        });
        target.Paused += (_, _) => PostPlayerEvent(target, () => { SetStatus("paused"); UpdateMiniPlayer(); });
        target.EncounteredError += (_, _) => PostPlayerEvent(target, () => SetStatus("audioStreamError"));
        target.EndReached += (_, _) => PostPlayerEnded(target);
    }

    private void PostPlayerEnded(MediaPlayer target)
    {
        if (closing || !IsHandleCreated) return;
        var request = playbackRequest;
        try
        {
            BeginInvoke((Action)(async () => await HandlePlayerEndedAsync(target, request)));
        }
        catch (InvalidOperationException) when (closing || !IsHandleCreated) { }
    }

    private async Task HandlePlayerEndedAsync(MediaPlayer target, CancellationTokenSource request)
    {
        if (closing || paused || !ReferenceEquals(request, playbackRequest)) return;
        if (ReferenceEquals(target, incomingPlayer))
        {
            CompleteCrossfade();
            await NextAsync();
            return;
        }
        if (!ReferenceEquals(target, player)) return;
        if (!isCrossfading) { await NextAsync(); return; }
        currentTrackEnded = true;
        if (incomingPlayer is not null) CompleteCrossfade();
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
            UpdateCrossfadeVolumes();
            return;
        }

        if (CanStartCrossfade()) await StartCrossfadeAsync();
    }

    private void UpdateCrossfadeVolumes()
    {
        if (incomingPlayer is null) return;
        var progress = Math.Min(1d, crossfadeElapsed.Elapsed.TotalMilliseconds / 5000d);
        player.Volume = (int)Math.Round(volume.Value * (1d - progress));
        incomingPlayer.Volume = (int)Math.Round(volume.Value * progress);
        if (progress >= 1d) CompleteCrossfade();
    }

    private bool CanStartCrossfade()
    {
        if (!settings.Crossfade || currentPlaylist is null || currentTrack is null) return false;
        if (currentPlaylist.Tracks.Count < 2 || player.Length <= 0 || player.Time <= 0) return false;
        return !ReferenceEquals(crossfadeAttemptedTrack, currentTrack) && player.Length - player.Time <= 5000;
    }

    private async Task StartCrossfadeAsync()
    {
        var token = playbackRequest.Token;
        isCrossfading = true;
        crossfadeAttemptedTrack = currentTrack;
        try
        {
            await PrepareIncomingTrackAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { await HandleCrossfadeFailureAsync(ex, token); }
    }

    private async Task PrepareIncomingTrackAsync(CancellationToken token)
    {
        var next = GetNextTrack();
        if (next is null) { isCrossfading = false; return; }
        incomingMedia = new Media(vlc, await ResolveAudioAsync(next, token));
        if (settings.NormalizeAudio) incomingMedia.AddOption(":audio-filter=compressor");
        incomingPlayer = new MediaPlayer(vlc) { Volume = 0, Mute = false };
        AttachPlayerEvents(incomingPlayer);
        if (!incomingPlayer.Play(incomingMedia)) throw new InvalidOperationException(S("libvlcCouldNotOpenTheNextStream"));
        incomingTrack = next;
        crossfadeElapsed.Restart();
        if (paused) { incomingPlayer.SetPause(true); crossfadeElapsed.Stop(); }
        else if (currentTrackEnded) CompleteCrossfade();
    }

    private async Task HandleCrossfadeFailureAsync(Exception error, CancellationToken token)
    {
        if (closing || token.IsCancellationRequested) return;
        incomingPlayer?.Dispose(); incomingPlayer = null;
        incomingMedia?.Dispose(); incomingMedia = null;
        isCrossfading = false;
        SetStatus("crossfadeMessage", Short(error.Message));
        if (!paused && currentTrackEnded) await NextAsync();
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
        SetStatus("playingTrack", currentTrack?.Name, PlaybackSource());
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

}
