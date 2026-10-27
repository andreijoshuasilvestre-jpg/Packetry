using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hand-editable SfxId → clip mapping behind AudioManager — the SFX
/// counterpart of MusicLibrary, so clips can live in Assets/SFX/ and be
/// assigned by dragging them onto an entry in the Inspector. AudioManager
/// loads Assets/Resources/SfxLibrary.asset via Resources.Load; an id with no
/// entry here falls back to the older Assets/Resources/Audio/&lt;id&gt;
/// naming convention, and an id with neither just stays silent.
/// </summary>
[CreateAssetMenu(fileName = "SfxLibrary", menuName = "Capstone/SFX Library")]
public class SfxLibrary : ScriptableObject
{
    public List<SfxEntry> sounds = new List<SfxEntry>();

    public SfxEntry Find(SfxId id)
    {
        foreach (var s in sounds)
            if (s != null && s.id == id && s.clip != null) return s;
        return null;
    }
}

[System.Serializable]
public class SfxEntry
{
    public SfxId id;
    public AudioClip clip;
    [Tooltip("Multiplier on the player's SFX volume setting — for balancing one clip against the rest.")]
    [Range(0f, 1f)] public float volume = 1f;
}
