using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Hand-editable data behind the level-select screen — one LevelEntry per
/// card (title/description/category/icons/order), editable from the
/// Inspector instead of the hardcoded list LevelSelectManager used to carry.
/// MainMenuUIBaker seeds Assets/Resources/LevelCatalog.asset with the
/// current default list the first time it runs (never overwriting it on
/// later re-runs, so hand edits survive); LevelSelectManager.Build() loads
/// it via Resources.Load, falling back to its own small hardcoded default
/// list if the asset is missing entirely.
/// </summary>
[CreateAssetMenu(fileName = "LevelCatalog", menuName = "Capstone/Level Catalog")]
public class LevelCatalog : ScriptableObject
{
    public List<LevelEntry> levels = new List<LevelEntry>();
}
