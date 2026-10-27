using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// The narrator: reads annotations aloud — tutorial callouts
/// (TutorialUIManager), concept cards (ConceptCardManager) and warning windows
/// (AnnotationWarningManager) — while the player has narration enabled
/// (Settings → Tutorial Narration, saved as SettingsData.narrationEnabled).
///
/// Clips are pre-generated, not synthesized at runtime:
/// Tools/Narration/generate_narration.py voices every line listed in
/// Tools/Narration/lines.json (Kokoro TTS, voice "af_heart") into
/// Assets/Resources/Narration/&lt;key&gt;.ogg, key being KeyFor(the line's
/// on-screen text). Lookup is by text alone, so no annotation needs an id —
/// and a line whose text was edited plays nothing (logging which line, once)
/// until it's added to lines.json and the script re-run, rather than reading
/// out stale words.
///
/// One voice at a time: Speak replaces whatever was playing. Each caller passes
/// itself as the speaker, so Stop, the typing sync (TextTypewriter) and the
/// speaking indicator (NarrationWaveform) only ever act on their own line.
/// Has its own volume (Settings → Narration Volume) rather than following the
/// SFX slider — a player who turns the clicks and blips down still hears the
/// narrator; the Tutorial Narration switch is its on/off.
/// </summary>
public class NarrationManager : MonoBehaviour
{
    public static NarrationManager Instance { get; private set; }

    public static bool Enabled => SaveManager.Settings.narrationEnabled;

    const string ClipFolder = "Narration/";

    // Typed text runs this far ahead of the voice (see SpokenFraction): the
    // eye reading just ahead of the ear feels in sync, text trailing it late.
    const float TextLeadSeconds = 0.2f;

    const string LevelPromptMessage =
        "This level is narrated — a voice reads each tip aloud. You can change this anytime in Settings.";

    // Levels already asked about this play session (static: survives the
    // scene reload a Restart does, so restarting doesn't ask again).
    static readonly HashSet<int> promptedLevels = new();

    static readonly Regex Tags   = new Regex("<[^>]*>", RegexOptions.Compiled);
    static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);

    AudioSource source;
    object speaker;
    readonly Dictionary<string, AudioClip> clips = new(); // null entries cache a known-missing clip

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake  = false;
        source.spatialBlend = 0f; // 2D
        source.volume       = SaveManager.Settings.narrationVolume;
    }

    // ── Speaking ─────────────────────────────────────────────────────────────

    /// <summary>Reads text aloud on speaker's behalf, cutting off whatever was being
    /// said. Silent when narration is off or the line has no clip.</summary>
    public void Speak(object speaker, string text)
    {
        Silence();
        if (!Enabled || string.IsNullOrWhiteSpace(text)) return;

        AudioClip clip = ClipFor(text);
        if (clip == null) return;

        this.speaker = speaker;
        source.clip = clip;
        source.Play();
    }

    /// <summary>Stops the voice — but only if it's speaker's line, so closing one
    /// annotation never cuts off another's.</summary>
    public void Stop(object speaker)
    {
        if (speaker != null && speaker == this.speaker) Silence();
    }

    public bool IsSpeaking(object speaker) => speaker != null && speaker == this.speaker && source.isPlaying;

    /// <summary>How far through speaker's line the voice is, 0 to 1, nudged a beat
    /// ahead (TextLeadSeconds) — what TextTypewriter paces its reveal by. 1 when
    /// speaker isn't speaking.</summary>
    public float SpokenFraction(object speaker) =>
        IsSpeaking(speaker) ? Mathf.Clamp01((source.time + TextLeadSeconds) / source.clip.length) : 1f;

    /// <summary>Fills spectrum with the voice's live frequency spectrum while it's
    /// speaker's line playing — NarrationWaveform's input. False otherwise.</summary>
    public bool ReadSpectrum(object speaker, float[] spectrum)
    {
        if (!IsSpeaking(speaker)) return false;
        source.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
        return true;
    }

    /// <summary>Turns narration on or off, saved. Turning it off silences the line
    /// being read right now.</summary>
    public static void SetEnabled(bool enabled)
    {
        SaveManager.Settings.narrationEnabled = enabled;
        SaveManager.Save();
        if (!enabled && Instance != null) Instance.Silence();
    }

    /// <summary>Live update while Settings' Narration Volume slider is dragged — in
    /// memory only, like AudioManager.SetVolume (the panel saves once per drag).
    /// Static so the main menu's Settings page can set it with no narrator around.</summary>
    public static void SetVolume(float volume)
    {
        SaveManager.Settings.narrationVolume = Mathf.Clamp01(volume);
        if (Instance != null) Instance.source.volume = SaveManager.Settings.narrationVolume;
    }

    void Silence()
    {
        source.Stop();
        source.clip = null;
        speaker = null;
    }

    // ── Clips ────────────────────────────────────────────────────────────────

    /// <summary>A line's clip name: 64-bit FNV-1a over its UTF-8 text with rich-text
    /// tags stripped and whitespace collapsed — so &lt;b&gt;/&lt;key&gt; styling, or
    /// the key-chip markup TutorialUIManager wraps keys in, never changes which
    /// clip plays. Must match clip_key() in Tools/Narration/generate_narration.py.</summary>
    public static string KeyFor(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(Normalize(text)))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }

    static string Normalize(string text) => Spaces.Replace(Tags.Replace(text, ""), " ").Trim();

    AudioClip ClipFor(string text)
    {
        string key = KeyFor(text);
        if (clips.TryGetValue(key, out var clip)) return clip;

        clip = Resources.Load<AudioClip>(ClipFolder + key);
        clips[key] = clip;
        if (clip == null)
            Debug.LogWarning($"[Narration] No voice clip for \"{Normalize(text)}\" — add it to Tools/Narration/lines.json and run generate_narration.py.");
        return clip;
    }

    // ── Level-start heads-up ─────────────────────────────────────────────────

    /// <summary>Run by GameBootstrap.EntrySequence before a narrated level's guidance
    /// starts: with narration on, tells the player the level is narrated and lets
    /// them switch to text only — once per level per session. Finishes once
    /// they've answered.</summary>
    public IEnumerator ConfirmForLevel(int level)
    {
        var dialog = ConfirmDialogManager.Instance;
        if (!Enabled || dialog == null || !promptedLevels.Add(level)) yield break;

        dialog.Show(LevelPromptMessage, "Keep It On", null,
            cancelText: "Text Only", onCancelClicked: () => SetEnabled(false), confirmColor: GameTheme.ElectricBlue);
        while (dialog.IsOpen) yield return null;
    }
}
