using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every sound effect the game can play. SfxLibrary.asset stores these as
/// ints — append new ids at the end rather than inserting or reordering, or
/// every existing clip assignment shifts onto the wrong sound.
/// </summary>
public enum SfxId
{
    UIClick,          // any UI Button clicked (UIButtonSounds)
    AnnotationTyping, // looped under the tutorial callout's typewriter reveal (StartLoop/StopLoop)
    PanelPrompt,      // a panel or dialog popping up (ZoomPanel, PanelRevealFX, ...)
    Place,            // the player placing a new device, table or cable
    TaskChecked,
    PacketSuccess,    // a ping or packet landing on its destination
    PingError,        // a failed ping or packet send
    StarAppear,       // one star filling in — LevelCompletePanel and the level select celebration
    StarCountUpdate,  // the level select header's star total ticking up
    LevelUnlocked,
    Remove,           // the player removing a device, table or cable — Place's clip reversed
    UIHover,          // the mouse moving onto an interactable UI Button (UIButtonSounds)
    CableConnecting,  // a player's new cable starting its draw animation (Place follows when it plugs in)
    Refusal,          // the game refusing a player action — invalid drop/placement/address, CLI error, ...
    IpAssigned,       // an IP address applied to a PC (popup) or router interface (CLI)
    LevelCompletePrompt, // LevelCompletePanel opening — replaces PanelPrompt there
    PacketHop,        // a ping or packet passing through a mid-path switch/router
    Tagging,          // a PC tagged for a ping or as a packet sender/receiver
    PacketTravel,     // looped while any ping or packet is in flight (PacketManager)
}

/// <summary>
/// Plays SfxIds. Each id's clip (and its volume trim) comes from
/// Assets/Resources/SfxLibrary.asset (see SfxLibrary), so clips can live in
/// Assets/SFX/ and be assigned in the Inspector; an id with no library entry
/// falls back to Assets/Resources/Audio/&lt;SfxId&gt;.wav (or .mp3) via
/// Resources.Load. Missing clips log a warning once and are otherwise
/// silently skipped, since the game works fine with any given sound absent.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // Repeats of one id closer together than this play once — a Run Network
    // batch can fail or deliver several packets in the same frame, and
    // stacking identical one-shots just makes that one sound louder.
    const float MinRepeatInterval = 0.06f;

    AudioSource source;
    float volume = SettingsData.DefaultVolume;
    readonly Dictionary<SfxId, (AudioClip clip, float volume)> clips = new();
    readonly Dictionary<SfxId, float> lastPlayed = new();
    readonly Dictionary<SfxId, AudioSource> loops = new();
    readonly HashSet<SfxId> warnedMissing = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D

        // Every scene that has an AudioManager gets button click/hover sounds.
        gameObject.AddComponent<UIButtonSounds>();

        // Rebuilt fresh every scene load (not DontDestroyOnLoad, unlike
        // MusicManager — see GameBootstrap/MainMenuBootstrap), so this has to
        // re-read the saved volume every time rather than relying on it surviving.
        volume = SaveManager.Settings.sfxVolume;

        // Resolved here rather than in Start() — other managers' Start() can
        // already open a panel (and so play a sound) before this one's runs.
        var library = Resources.Load<SfxLibrary>("SfxLibrary");
        foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
        {
            var entry = library != null ? library.Find(id) : null;
            if (entry != null) { clips[id] = (entry.clip, entry.volume); continue; }

            var clip = Resources.Load<AudioClip>($"Audio/{id}");
            if (clip != null) clips[id] = (clip, 1f);
        }
    }

    /// <summary>Live update while SettingsPanelManager's slider is being dragged.
    /// Only updates the in-memory setting — writing save.json on every frame of a
    /// drag would serialize the whole save (every level's topology) that often;
    /// SettingsPanelManager calls SaveManager.Save() once when the drag ends.</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        SaveManager.Settings.sfxVolume = volume;

        foreach (var kv in loops)
            if (clips.TryGetValue(kv.Key, out var c)) kv.Value.volume = volume * c.volume;
    }

    public float Volume => volume;

    public void PlaySfx(SfxId id)
    {
        if (!TryGetClip(id, out var c)) return;

        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(id, out float last) && now - last < MinRepeatInterval) return;
        lastPlayed[id] = now;

        source.PlayOneShot(c.clip, volume * c.volume);
    }

    /// <summary>Loops id on its own AudioSource until StopLoop(id) — for a
    /// sound that should last exactly as long as some animation (the tutorial
    /// callout's typewriter reveal) rather than for its clip's length. Calling
    /// it again while already looping just keeps the loop going, so text that
    /// restarts its reveal mid-way doesn't make the sound stutter.</summary>
    public void StartLoop(SfxId id)
    {
        if (!TryGetClip(id, out var c)) return;

        if (!loops.TryGetValue(id, out var loop))
        {
            loop = gameObject.AddComponent<AudioSource>();
            loop.playOnAwake  = false;
            loop.spatialBlend = 0f; // 2D
            loop.loop         = true;
            loops[id] = loop;
        }

        loop.volume = volume * c.volume;
        if (loop.isPlaying && loop.clip == c.clip) return;
        loop.clip = c.clip;
        loop.Play();
    }

    public void StopLoop(SfxId id)
    {
        // `loop != null` (Unity's destroyed-aware check, which a caller's
        // Instance?. doesn't do) — on scene unload another object's OnDisable
        // can land here after this manager's AudioSources are already gone.
        if (loops.TryGetValue(id, out var loop) && loop != null) loop.Stop();
    }

    bool TryGetClip(SfxId id, out (AudioClip clip, float volume) c)
    {
        if (clips.TryGetValue(id, out c) && c.clip != null) return true;

        if (warnedMissing.Add(id))
            Debug.LogWarning($"[AudioManager] No clip for {id} — assign one in Assets/Resources/SfxLibrary.asset to enable this sound.");
        return false;
    }
}
