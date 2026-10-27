using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hand-editable MusicId → clip mapping behind MusicManager, so tracks can
/// live anywhere in the project (e.g. Assets/Music/) and be assigned by
/// dragging them onto an entry in the Inspector. MusicManager loads
/// Assets/Resources/MusicLibrary.asset via Resources.Load; an id with no
/// entry here falls back to the older Assets/Resources/Audio/Music/&lt;id&gt;
/// naming convention.
/// </summary>
[CreateAssetMenu(fileName = "MusicLibrary", menuName = "Capstone/Music Library")]
public class MusicLibrary : ScriptableObject
{
    public List<MusicEntry> tracks = new List<MusicEntry>();

    public MusicEntry Find(MusicId id)
    {
        foreach (var t in tracks)
            if (t != null && t.id == id && t.clip != null) return t;
        return null;
    }
}

[System.Serializable]
public class MusicEntry
{
    public MusicId id;
    public AudioClip clip;
    [Tooltip("Seconds of silence after the clip ends before it plays again.")]
    [Min(0f)] public float loopDelay = 1f;
}
