/// <summary>
/// Marks a level's objective provider (see GameBootstrap.ObjectiveProviderFor)
/// whose guidance is narrated — a guided walkthrough or a concept card — so
/// the level opens with NarrationManager's heads-up before its first line is
/// spoken. A level that only ever narrates the odd warning window doesn't
/// need it.
/// </summary>
public interface INarratedLevel { }
