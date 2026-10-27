using UnityEngine;

/// <summary>
/// Marks a Button that UIButtonSounds should leave silent — one that isn't a
/// button to the player, like a full-screen click-outside-to-close catcher
/// (CompoundSidebarGroup's flyout backdrop, SettingsPanelManager's color
/// picker). Without it, the catcher covering the screen would count as
/// "hovering a button" the moment it opens, and dismissing would click.
/// </summary>
public class SilentUIButton : MonoBehaviour { }
