using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Starter SFX set — later phases (ping tool, level win/lose, device
/// place/remove) add more ids as they're wired up.
/// </summary>
public enum SfxId { UIClick, Success, Fail }

/// <summary>
/// Loads clips from Assets/Resources/Audio/&lt;SfxId&gt;.wav (or .mp3) via
/// Resources.Load, so adding a sound is just dropping a correctly-named file
/// in that folder — no scene/inspector wiring needed. Missing clips log a
/// warning once and are otherwise silently skipped, since most of the game
/// works fine with no audio until real clips are supplied.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    AudioSource source;
    float volume = 0.8f;
    readonly Dictionary<SfxId, AudioClip> clips = new();
    readonly HashSet<SfxId> warnedMissing = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D

        // Managers rebuild fresh every scene load (no DontDestroyOnLoad anywhere
        // in this project — see GameBootstrap/MainMenuBootstrap), so this has to
        // re-read the saved volume every time rather than relying on it surviving.
        volume = SaveManager.Settings.sfxVolume;
    }

    /// <summary>Live update while SettingsPanelManager's slider is being dragged.
    /// Only updates the in-memory setting — writing save.json on every frame of a
    /// drag would serialize the whole save (every level's topology) that often;
    /// SettingsPanelManager calls SaveManager.Save() once when the drag ends.</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        SaveManager.Settings.sfxVolume = volume;
    }

    public float Volume => volume;

    void Start()
    {
        foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
        {
            var clip = Resources.Load<AudioClip>($"Audio/{id}");
            if (clip != null) clips[id] = clip;
        }
    }

    public void PlaySfx(SfxId id)
    {
        if (clips.TryGetValue(id, out var clip) && clip != null)
        {
            source.PlayOneShot(clip, volume);
            return;
        }

        if (warnedMissing.Add(id))
            Debug.LogWarning($"[AudioManager] No clip for {id} — add Assets/Resources/Audio/{id}.wav to enable this sound.");
    }
}
