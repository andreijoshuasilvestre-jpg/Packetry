/// <summary>
/// The router CLI's plain-language command explanations — what each command
/// in RouterCLI's command tables does, why you'd use it, its parameters, an
/// example, and where it takes you. Kept apart from RouterCLI's interpreter
/// so the teaching text can be edited without touching command parsing; keyed
/// by the exact command-table names ("interface eth&lt;N&gt;", "ip address
/// &lt;ip&gt; &lt;mask&gt;"). A command missing here just shows its one-line
/// table description instead.
/// </summary>
public static class RouterCommandDocs
{
    public static CLICommandDoc For(string command, RouterCLI.Mode mode, int portCount, string hostname)
    {
        CLICommandDoc doc = Build(command, mode, portCount);
        if (doc == null) return null;

        doc.PromptBefore = RouterCLI.PromptFor(hostname, mode);
        RouterCLI.Mode? target = TargetMode(command, mode, out bool closes);
        if (closes) doc.PromptAfter = "";
        else if (target.HasValue && target.Value != mode) doc.PromptAfter = RouterCLI.PromptFor(hostname, target.Value);
        return doc;
    }

    // Where a command leaves the session — mirrors RouterCLI's Exec* switches.
    static RouterCLI.Mode? TargetMode(string command, RouterCLI.Mode mode, out bool closes)
    {
        closes = false;
        switch (command)
        {
            case "enable":             return RouterCLI.Mode.PrivilegedExec;
            case "configure terminal": return RouterCLI.Mode.GlobalConfig;
            case "interface eth<N>":   return RouterCLI.Mode.InterfaceConfig;
            case "end":                return RouterCLI.Mode.PrivilegedExec;
            case "exit":
                switch (mode)
                {
                    case RouterCLI.Mode.GlobalConfig:    return RouterCLI.Mode.PrivilegedExec;
                    case RouterCLI.Mode.InterfaceConfig: return RouterCLI.Mode.GlobalConfig;
                    default: closes = true; return null;
                }
            default: return null;
        }
    }

    static CLICommandDoc Build(string command, RouterCLI.Mode mode, int portCount)
    {
        switch (command)
        {
            case "enable":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Move,
                    What = "Unlocks Privileged mode. Every session starts in User mode, which can only look " +
                           "around. Privileged mode can see everything, and it's the way into configuration.",
                    Note = "Real routers usually ask for a password here.",
                };

            case "exit":
                switch (mode)
                {
                    case RouterCLI.Mode.GlobalConfig:
                        return new CLICommandDoc
                        {
                            Kind = CliCommandKind.Move,
                            What = "Goes back one level, from Global Config to Privileged mode.",
                        };
                    case RouterCLI.Mode.InterfaceConfig:
                        return new CLICommandDoc
                        {
                            Kind = CliCommandKind.Move,
                            What = "Goes back one level, from this port's settings to Global Config.",
                            Note = "To leave configuration completely in one step, use end.",
                        };
                    default:
                        return new CLICommandDoc
                        {
                            Kind = CliCommandKind.Move,
                            What = "Closes this command line. Everything you've configured stays on the router.",
                        };
                }

            case "?":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Help,
                    What = "Lists every command you can use right now, each with a one-line description. " +
                           "The list changes as you move between modes.",
                };

            case "configure terminal":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Move,
                    What = "Enters Global Config, where you change the router's settings. \"terminal\" means " +
                           "you'll type those settings here, one line at a time.",
                    Note = "Short form: conf t",
                };

            case "show ip interface brief":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.View,
                    What = "Prints a table of every port: its IP address, and whether it's up or switched off " +
                           "(\"administratively down\"). Use it to check your work. It changes nothing.",
                    Example = "eth0   192.168.1.1   up",
                };

            case "interface eth<N>":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Move,
                    What = mode == RouterCLI.Mode.InterfaceConfig
                        ? "Jumps straight to a different port without leaving Interface mode."
                        : "Picks one port to set up and enters Interface mode. Every command after this " +
                          "applies only to that port, and its cable lights up so you can see which one it is.",
                    Params = new[]
                    {
                        ("<N>", portCount > 1
                            ? $"Port number, 0 to {portCount - 1}. eth0 is the first port."
                            : "Port number. eth0 is the first port."),
                    },
                    Example = "interface eth0",
                };

            case "end":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Move,
                    What = "Leaves configuration completely and jumps straight back to Privileged mode, " +
                           "from any configuration level.",
                };

            case "ip address <ip> <mask>":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Change,
                    What = "Gives this port an IP address. Devices on the network behind this port use that " +
                           "address as their gateway, their way out of their own subnet.",
                    Params = new[]
                    {
                        ("<ip>",   "This port's own address, e.g. 192.168.1.1"),
                        ("<mask>", "Subnet mask: which addresses count as the same network, e.g. 255.255.255.0"),
                    },
                    Example = "ip address 192.168.1.1 255.255.255.0",
                    Note = "Ports start switched off. Follow up with no shutdown.",
                };

            case "shutdown":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Change,
                    What = "Switches this port off. Nothing crosses it until it's turned back on, but its " +
                           "IP address is kept.",
                    Note = "Undo it with no shutdown.",
                };

            case "no shutdown":
                return new CLICommandDoc
                {
                    Kind = CliCommandKind.Change,
                    What = "Switches this port on. Router ports start switched off, so every port you use " +
                           "needs this. Putting \"no\" in front of a router command undoes it.",
                };

            default:
                return null;
        }
    }

    /// <summary>One rung of the router's mode ladder — see RouterCLI.ModeLadder.</summary>
    public static CLIModeDoc ForMode(RouterCLI.Mode mode, string hostname)
    {
        string prompt = RouterCLI.PromptFor(hostname, mode);
        switch (mode)
        {
            case RouterCLI.Mode.UserExec:
                return new CLIModeDoc
                {
                    Label = "USER", Name = "User EXEC", Prompt = prompt,
                    What = "Look-only mode, where every session starts. Nothing can be changed from here.",
                    HowToMove = "Type enable to go deeper.",
                };
            case RouterCLI.Mode.PrivilegedExec:
                return new CLIModeDoc
                {
                    Label = "PRIVILEGED", Name = "Privileged EXEC", Prompt = prompt,
                    What = "Full-access mode. You can view everything, and it's the doorway into configuration.",
                    HowToMove = "Reached with enable. Type configure terminal to start configuring.",
                };
            case RouterCLI.Mode.GlobalConfig:
                return new CLIModeDoc
                {
                    Label = "CONFIG", Name = "Global Config", Prompt = prompt,
                    What = "Settings for the router as a whole. To set up one port, pick it with interface.",
                    HowToMove = "Reached with configure terminal. end jumps back to Privileged.",
                };
            default:
                return new CLIModeDoc
                {
                    Label = "INTERFACE", Name = "Interface Config", Prompt = prompt,
                    What = "Settings for one port: its IP address, and whether it's switched on.",
                    HowToMove = "Reached with interface eth<N>. exit goes back one level; end leaves configuration.",
                };
        }
    }
}
