using UnityEngine;

/// <summary>Which looping track to play — one per scene "mood" (see GameBootstrap/
/// MainMenuBootstrap, each of which calls Play() with its own scene's id once).</summary>
public enum MusicId { MainMenu, Gameplay }

/// <summary>
/// Loops background music. Each MusicId's clip (and its loop delay) comes from
/// Assets/Resources/MusicLibrary.asset (see MusicLibrary), so tracks can live
/// in Assets/Music/ and be assigned in the Inspector; an id with no library
/// entry falls back to Assets/Resources/Audio/Music/&lt;MusicId&gt;.wav (or .mp3)
/// via Resources.Load. No clip yet is not an error: Play() just stays silent
/// (warning once), same as AudioManager.PlaySfx's missing-clip path, since the
/// volume slider needs to exist before any real music does.
///
/// Unlike the other managers, survives scene loads (DontDestroyOnLoad, like
/// LoadingScreenManager): each scene's bootstrap still calls Play() with its
/// own track, which is a no-op when that track is already playing — so
/// restarting a level (LoadingScreenManager.RestartLevel reloads the same
/// scene) carries the music straight through the reload instead of
/// starting it over, while menu ↔ level still switches tracks.
/// GameBootstrap/MainMenuBootstrap's EnsureComponent finds the surviving
/// instance rather than building a second one.
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    AudioSource source;
    MusicLibrary library;
    float volume = SettingsData.DefaultVolume;
    MusicId? currentTrack;
    bool  looping;      // a track is active and should keep repeating
    float loopDelay;    // silence between repeats; 0 = seamless AudioSource.loop
    float silenceTimer;
    readonly System.Collections.Generic.HashSet<MusicId> warnedMissing = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject); // see class doc — keeps a restart from cutting the music

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake  = false;
        source.spatialBlend = 0f; // 2D

        library = Resources.Load<MusicLibrary>("MusicLibrary");

        volume = SaveManager.Settings.musicVolume;
        source.volume = volume;
    }

    // AudioSource.loop can't leave a gap between repeats, so a track with a
    // loopDelay plays once and is restarted here after that much silence.
    // Unscaled time so a paused game (timeScale 0) doesn't stall the music.
    void Update()
    {
        if (!looping || source.loop || source.isPlaying) return;
        silenceTimer += Time.unscaledDeltaTime;
        if (silenceTimer < loopDelay) return;
        silenceTimer = 0f;
        source.Play();
    }

    /// <summary>Starts (or restarts, if a different track is already playing) the
    /// given track looping. Calling it again with the same track that's already
    /// playing is a no-op, so scene code can call this freely without tracking
    /// whether it already did.</summary>
    public void Play(MusicId id)
    {
        if (currentTrack == id && looping) return;
        currentTrack = id;

        var entry = library != null ? library.Find(id) : null;
        var clip  = entry != null ? entry.clip : Resources.Load<AudioClip>($"Audio/Music/{id}");
        if (clip == null)
        {
            looping = false;
            source.Stop();
            if (warnedMissing.Add(id))
                Debug.LogWarning($"[MusicManager] No clip for {id} — assign one in Assets/Resources/MusicLibrary.asset to enable background music.");
            return;
        }

        loopDelay    = entry != null ? entry.loopDelay : 0f;
        silenceTimer = 0f;
        looping      = true;

        source.clip   = clip;
        source.loop   = loopDelay <= 0f;
        source.volume = volume;
        source.Play();
    }

    public void Stop()
    {
        currentTrack = null;
        looping = false;
        source.Stop();
    }

    /// <summary>Live update while SettingsPanelManager's slider is being dragged.
    /// Only updates the in-memory setting — writing save.json on every frame of a
    /// drag would serialize the whole save (every level's topology) that often;
    /// SettingsPanelManager calls SaveManager.Save() once when the drag ends.</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        source.volume = volume;
        SaveManager.Settings.musicVolume = volume;
    }

    public float Volume => volume;
}
