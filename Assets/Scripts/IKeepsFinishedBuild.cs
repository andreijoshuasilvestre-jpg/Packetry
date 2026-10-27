/// <summary>
/// Marks a level objective provider whose finished run is the player's own
/// design — a challenge level's topology, built from scratch or repaired.
/// Re-entering such a level after completing it restores that build instead
/// of replaying from the level's default content (see
/// GameBootstrap.SetupLevelContent); the HUD's Restart still starts it over.
/// Guided walkthroughs (Level2Guide..Level4Guide) don't implement it: a
/// finished walkthrough has nothing left to guide, so it replays fresh.
/// </summary>
public interface IKeepsFinishedBuild { }
