using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the current level's task list and evaluates each task's completion
/// condition every frame. UI (see TaskUIManager) reads Tasks/IsComplete to
/// render checkboxes — mirrors how CableManager owns IsConnecting and
/// NetworkUIManager just reads it.
///
/// Deliberately level-agnostic: it knows nothing about which level is active
/// or what its objectives are. Level-specific providers (e.g. Level2Guide,
/// Level3Guide, instantiated by GameBootstrap.SetupLevelObjectives only when
/// that level is
/// current) call RegisterTask with their own conditions and own whatever
/// device/event tracking those conditions need — same as TutorialManager
/// independently tracking placed devices for its own step conditions.
/// </summary>
public class TaskManager : MonoBehaviour
{
    public static TaskManager Instance { get; private set; }

    public class GameTask
    {
        public string Description;
        public Func<bool> CheckCondition;
        public bool IsComplete;

        // Optional 3-tier hint ladder — null/empty tiers just don't render a
        // hint affordance for that tier (see TaskUIManager.CreateRow). Tier 1
        // is a nudge, tier 2 gets specific, tier 3 is the answer.
        public string HintNudge;
        public string HintSpecific;
        public string HintAnswer;
        // 0 = no hint used yet. Drives both TaskUIManager's per-row hint text
        // and ComputeStars()'s level-completion star rating.
        public int HighestHintTierUsed;
    }

    private readonly List<GameTask> tasks = new List<GameTask>();
    public IReadOnlyList<GameTask> Tasks => tasks;

    public event Action OnTasksChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // hintNudge/hintSpecific/hintAnswer are optional trailing params on this
    // same method rather than a separate overload — fully source-compatible
    // with every existing call site that only passes the first two args.
    public void RegisterTask(string description, Func<bool> checkCondition,
                              string hintNudge = null, string hintSpecific = null, string hintAnswer = null)
    {
        tasks.Add(new GameTask
        {
            Description = description, CheckCondition = checkCondition,
            HintNudge = hintNudge, HintSpecific = hintSpecific, HintAnswer = hintAnswer,
        });
        OnTasksChanged?.Invoke();
    }

    /// <summary>Reveals hint tier N (1-3) for a task, if not already at or past it.
    /// Called by TaskUIManager's per-row "Hint" affordance.</summary>
    public void UseHint(GameTask task, int tier)
    {
        if (task == null || tier <= task.HighestHintTierUsed) return;
        task.HighestHintTierUsed = tier;
        OnTasksChanged?.Invoke();
    }

    /// <summary>3 stars if no task in the current level ever needed a hint, 2 if the
    /// deepest hint used anywhere was just a nudge (tier 1), 1 if any task needed a
    /// specific/answer-tier hint (tier 2 or 3).</summary>
    public int ComputeStars()
    {
        if (tasks.Count == 0) return 3;
        int deepest = 0;
        foreach (var t in tasks) deepest = Mathf.Max(deepest, t.HighestHintTierUsed);
        return 3 - Mathf.Clamp(deepest, 0, 2);
    }

    void Update()
    {
        bool changed = false;
        foreach (var task in tasks)
        {
            if (task.IsComplete || task.CheckCondition == null) continue;
            if (task.CheckCondition())
            {
                task.IsComplete = true;
                changed = true;
            }
        }
        if (changed) OnTasksChanged?.Invoke();
    }
}
