using UnityEngine;

/// <summary>
/// What a CLI command does, at a glance: moves you to another mode, changes a
/// setting, only shows information, or is help. Shown as a colored tag on the
/// command's Command Builder chip and on its explanation card, so a player can
/// tell "this takes me somewhere" from "this changes the router" before
/// reading a word of the description. A device type's CLI tags its own
/// commands (see RouterCommandDocs).
/// </summary>
public enum CliCommandKind { Move, Change, View, Help }

public static class CliCommandKinds
{
    public static string Label(CliCommandKind kind) => kind switch
    {
        CliCommandKind.Move   => "MOVE",
        CliCommandKind.Change => "CHANGE",
        CliCommandKind.View   => "VIEW",
        _                     => "HELP",
    };

    public static Color Color(CliCommandKind kind) => kind switch
    {
        CliCommandKind.Move   => new Color(0.40f, 0.68f, 1.00f, 1f), // blue
        CliCommandKind.Change => new Color(1.00f, 0.66f, 0.28f, 1f), // orange
        CliCommandKind.View   => new Color(0.36f, 0.85f, 0.78f, 1f), // teal
        _                     => new Color(0.76f, 0.66f, 0.98f, 1f), // lavender
    };

    /// <summary>One line on what the tag itself means — the fallback footer of
    /// an explanation card, so the tags explain themselves too.</summary>
    public static string Meaning(CliCommandKind kind) => kind switch
    {
        CliCommandKind.Move   => "MOVE commands take you to another mode. Watch the prompt change.",
        CliCommandKind.Change => "CHANGE commands edit the router's settings.",
        CliCommandKind.View   => "VIEW commands only show information. Safe to run anytime.",
        _                     => "HELP commands change nothing.",
    };
}

/// <summary>
/// A full, plain-language explanation of one CLI command, as it applies from
/// the mode it's being looked at in ("exit" closes the CLI from one mode and
/// steps back a level from another). Built per lookup by the device's CLI —
/// see RouterCLI.Describe.
/// </summary>
public sealed class CLICommandDoc
{
    public CliCommandKind Kind;
    /// <summary>What it does and why you'd use it.</summary>
    public string What;
    /// <summary>One (name, meaning) pair per &lt;parameter&gt; in the command, or null.</summary>
    public (string name, string meaning)[] Params;
    /// <summary>A complete, runnable example line, or null.</summary>
    public string Example;
    /// <summary>One extra tip (a shortcut, a follow-up command), or null.</summary>
    public string Note;
    /// <summary>The prompt right now.</summary>
    public string PromptBefore;
    /// <summary>The prompt after running it — null when it stays the same,
    /// empty when the command closes the CLI instead.</summary>
    public string PromptAfter;
}

/// <summary>One rung of a CLI's mode ladder (User → Privileged → Config →
/// Interface for a router) — what the mode is for and how to get in/out.</summary>
public sealed class CLIModeDoc
{
    /// <summary>Short label for the ladder strip, e.g. "PRIVILEGED".</summary>
    public string Label;
    /// <summary>Full mode name, e.g. "Privileged EXEC".</summary>
    public string Name;
    public string Prompt;
    public string What;
    public string HowToMove;
}
