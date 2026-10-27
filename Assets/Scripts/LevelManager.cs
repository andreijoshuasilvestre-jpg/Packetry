using UnityEngine;

/// <summary>
/// Tracks which level the current scene load is for. SampleScene is the only
/// gameplay scene — levels beyond Level 1 don't get their own .unity file, they
/// share this one and differ only in which objective provider GameBootstrap
/// attaches (see GameBootstrap.SetupLevelObjectives) and whether TutorialManager
/// runs its guided walkthrough (see TutorialManager.Start).
///
/// PendingLevelNumber follows the same "plain static field set by the menu
/// side (LevelSelectManager.OnLevelClicked) immediately before the scene
/// load, consumed-and-reset here in Awake() of the freshly loaded scene" idiom
/// used elsewhere in this project (see MainMenuBootstrap.OpenLevelSelectOnLoad).
///
/// Defaults to DevSandboxLevel, not 1 — pressing Play directly on SampleScene in
/// the Editor (bypassing the menu entirely) is how you build/test freely, not how
/// you replay the tutorial, so it should land somewhere with every device/cable/
/// CLI tool unlocked and no tutorial/task gating, not Level 1's guided
/// walkthrough. Level select's own path always sets PendingLevelNumber
/// explicitly right before loading (see LevelSelectManager.OnLevelClicked), so
/// real players choosing a level from the menu never see this default.
/// </summary>
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    // An arbitrary level number that deliberately matches none of
    // GameBootstrap's level==N branches (SetupLevelContent/SetupLevelObjectives/
    // SetupUI) — so a direct Editor Play lands on a blank canvas with every tool
    // available and nothing hidden, restricted, or auto-completing behind it.
    // Also doubles as a safe scratch level number for
    // LevelTemplateAuthoringWindow test captures without touching any real
    // level's save data.
    public const int DevSandboxLevel = 99;

    public static int PendingLevelNumber = DevSandboxLevel;

    public int CurrentLevel { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        CurrentLevel = PendingLevelNumber;
        PendingLevelNumber = DevSandboxLevel;
    }
}
