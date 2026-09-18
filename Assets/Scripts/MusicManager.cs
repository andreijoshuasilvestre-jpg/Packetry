using UnityEngine;

/// <summary>Which looping track to play — one per scene "mood" (see GameBootstrap/
/// MainMenuBootstrap, each of which calls Play() with its own scene's id once).</summary>
public enum MusicId { MainMenu, Gameplay }

/// <summary>
/// Loops background music the same way AudioManager plays one-shot SFX: loads
/// Assets/Resources/Audio/Music/&lt;MusicId&gt;.wav (or .mp3) via Resources.Load,
/// so dropping a correctly-named clip in that folder is all that's needed —
/// no scene/inspector wiring. No clip yet is not an error: Play() just stays
/// silent (warning once), same as AudioManager.PlaySfx's missing-clip path,
/// since the volume slider needs to exist before any real music does.
///
/// Rebuilt fresh every scene load like every other manager here (no
/// DontDestroyOnLoad in this project) — a scene change simply restarts
/// whichever track that scene calls Play() with.
/// </summary>
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    AudioSource source;
    float volume = 0.8f;
    MusicId? currentTrack;
    readonly System.Collections.Generic.HashSet<MusicId> warnedMissing = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake  = false;
        source.spatialBlend = 0f; // 2D
        source.loop         = true;

        volume = SaveManager.Settings.musicVolume;
        source.volume = volume;
    }

    /// <summary>Starts (or restarts, if a different track is already playing) the
    /// given track looping. Calling it again with the same track that's already
    /// playing is a no-op, so scene code can call this freely without tracking
    /// whether it already did.</summary>
    public void Play(MusicId id)
    {
        if (currentTrack == id && source.isPlaying) return;
        currentTrack = id;

        var clip = Resources.Load<AudioClip>($"Audio/Music/{id}");
        if (clip == null)
        {
            source.Stop();
            if (warnedMissing.Add(id))
                Debug.LogWarning($"[MusicManager] No clip for {id} — add Assets/Resources/Audio/Music/{id}.wav to enable background music.");
            return;
        }

        source.clip = clip;
        source.volume = volume;
        source.Play();
    }

    public void Stop()
    {
        currentTrack = null;
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
