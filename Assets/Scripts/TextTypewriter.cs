using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// The typewriter reveal every annotation types its text in with — the
/// tutorial callout (TutorialUIManager) and the concept cards
/// (ConceptCardManager) — so they share one pace, one typing sound and one
/// set of rules instead of each carrying its own copy.
///
/// The full string is assigned up front (so auto-sizing and layout are
/// computed against the final text, not a growing substring) and TMP's
/// maxVisibleCharacters is animated from 0 up to the real character count —
/// no re-layout every frame, and rich-text tags never count as characters.
///
/// While the line is being narrated (see NarrationManager), the reveal follows
/// the voice instead of a fixed pace — the text keeping step with the words,
/// without the typing sound over the narrator.
/// </summary>
public static class TextTypewriter
{
    /// <summary>Reveals label's current text at charsPerSecond (unscaled time),
    /// with the annotation typing loop playing for exactly as long as characters
    /// are still appearing — or, while speaker's line is being narrated, in step
    /// with the voice and silently (picking up at charsPerSecond again if the
    /// voice is cut off early). Start the narration before this. Run it as a
    /// coroutine; a caller that stops it early calls Finish.</summary>
    public static IEnumerator Reveal(TMP_Text label, float charsPerSecond, object speaker = null)
    {
        label.ForceMeshUpdate();
        int totalVisible = label.textInfo.characterCount;
        label.maxVisibleCharacters = 0;

        var voice = NarrationManager.Instance;
        float shown = 0f;
        bool typingSound = false;
        while (shown < totalVisible)
        {
            bool voiced = voice != null && voice.IsSpeaking(speaker);
            shown = voiced
                ? Mathf.Max(shown, voice.SpokenFraction(speaker) * totalVisible)
                : shown + Time.unscaledDeltaTime * charsPerSecond;
            typingSound = SetTypingSound(typingSound, !voiced);
            label.maxVisibleCharacters = Mathf.Clamp(Mathf.FloorToInt(shown), 0, totalVisible);
            yield return null;
        }

        label.maxVisibleCharacters = totalVisible;
        SetTypingSound(typingSound, false);
    }

    static bool SetTypingSound(bool playing, bool play)
    {
        if (play && !playing) AudioManager.Instance?.StartLoop(SfxId.AnnotationTyping);
        else if (!play && playing) AudioManager.Instance?.StopLoop(SfxId.AnnotationTyping);
        return play;
    }

    /// <summary>Shows all of label's text at once and silences the typing loop —
    /// for a reveal cut short (the player skipped ahead, or the annotation closed).
    /// Stop the Reveal coroutine first.</summary>
    public static void Finish(TMP_Text label)
    {
        if (label != null) label.maxVisibleCharacters = 99999; // TMP's own "no limit" default
        AudioManager.Instance?.StopLoop(SfxId.AnnotationTyping);
    }
}
