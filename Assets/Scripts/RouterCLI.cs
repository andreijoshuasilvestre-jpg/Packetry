using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// Simplified Cisco IOS-style CLI for a router.
/// Covers mode switching, basic show/config commands, and per-interface
/// (per physical port) configuration.
/// </summary>
public class RouterCLI
{
    public enum Mode { UserExec, PrivilegedExec, GlobalConfig, InterfaceConfig }

    // ── Command tables (single source of truth for help text, tab-complete, and suggestions) ──

    static readonly (string name, string desc)[] UserExecCommands =
    {
        ("enable", "Unlock Privileged mode (needed to configure)"),
        ("exit",   "Close the command line"),
        ("?",      "List the commands you can use here"),
    };

    static readonly (string name, string desc)[] PrivilegedExecCommands =
    {
        ("configure terminal",      "Enter Global Config to change settings"),
        ("show ip interface brief", "Show every port's IP address and status"),
        ("exit",                    "Close the command line"),
        ("?",                       "List the commands you can use here"),
    };

    // Both depend on device.PortCount, so built per-instance rather than static.
    readonly (string name, string desc)[] _globalConfigCommands;
    readonly (string name, string desc)[] _interfaceConfigCommands;

    readonly NetworkDevice          _device;
    // The interface table itself lives on the GameObject (see
    // RouterInterfaceConfig's own doc comment for why) — RouterCLI just reads
    // and writes through it instead of owning the data. DeviceModelBuilder
    // already attaches one to every router it builds, but a router built some
    // other way (or an older saved scene) might not have one yet, hence the
    // fallback AddComponent below.
    readonly RouterInterfaceConfig  _ifaceConfig;
    readonly string _hostname;
    Mode   _mode;
    int    _currentPort;

    public RouterCLI(NetworkDevice device)
    {
        _device     = device;
        _hostname   = device.deviceName;
        _mode       = Mode.UserExec;

        _ifaceConfig = device.GetComponent<RouterInterfaceConfig>()
                     ?? device.gameObject.AddComponent<RouterInterfaceConfig>();
        _ifaceConfig.EnsureSize(device.PortCount);

        _globalConfigCommands = new (string name, string desc)[]
        {
            ("interface eth<N>", $"Pick a port to set up (eth0..eth{device.PortCount - 1})"),
            ("end",              "Leave configuration, back to Privileged"),
            ("exit",             "Back one level, to Privileged"),
            ("?",                "List the commands you can use here"),
        };

        // Includes "interface eth<N>" — real IOS lets you jump straight from
        // one interface's config mode into another's without exiting back to
        // global config first (e.g. straight from eth0's "no shutdown" into
        // "interface eth1"), which is exactly the move Level4Guide's own
        // stage 3 teaches — see ExecInterfaceCfg's "interface" case below.
        _interfaceConfigCommands = new (string name, string desc)[]
        {
            ("ip address <ip> <mask>", "Give this port an IP address and mask"),
            ("shutdown",               "Turn this port off"),
            ("no shutdown",            "Turn this port on"),
            ("interface eth<N>",       $"Switch to another port (eth0..eth{device.PortCount - 1})"),
            // ExecInterfaceCfg already accepts "end" (straight back to
            // Privileged EXEC) — listed here too so it can be dragged, which
            // Level3Guide's "type end" stage points its drag trail at.
            ("end",                    "Leave configuration, back to Privileged"),
            ("exit",                   "Back one level, to Global Config"),
            ("?",                      "List the commands you can use here"),
        };
    }

    public string Banner =>
        $"Cisco IOS Software, Version 15.2 (educational simulation)\n" +
        $"Router: {_hostname}\n" +
        "Type '?' for a list of available commands.";

    public Mode CurrentMode => _mode;

    /// <summary>Port index being edited while in InterfaceConfig mode.</summary>
    public int CurrentInterfacePort => _currentPort;

    public string Prompt => PromptFor(_hostname, _mode);

    /// <summary>The prompt a router shows in a given mode — also what the
    /// explanation cards use to show where a command takes you.</summary>
    public static string PromptFor(string hostname, Mode mode)
    {
        switch (mode)
        {
            case Mode.UserExec:        return $"{hostname}>";
            case Mode.PrivilegedExec:  return $"{hostname}#";
            case Mode.GlobalConfig:    return $"{hostname}(config)#";
            case Mode.InterfaceConfig: return $"{hostname}(config-if)#";
            default:                   return $"{hostname}>";
        }
    }

    /// <summary>The modes in order, outermost first — the CLI panel's mode
    /// ladder strip draws one rung per entry.</summary>
    public static readonly Mode[] ModeLadder =
        { Mode.UserExec, Mode.PrivilegedExec, Mode.GlobalConfig, Mode.InterfaceConfig };

    /// <summary>Plain-language explanation of one of CurrentCommands (by its
    /// exact table name) as it applies from the current mode, or null if it
    /// has none — see RouterCommandDocs.</summary>
    public CLICommandDoc Describe(string commandName) =>
        RouterCommandDocs.For(commandName, _mode, _device.PortCount, _hostname);

    /// <summary>What a mode is for and how to get in and out of it.</summary>
    public CLIModeDoc DescribeMode(Mode mode) => RouterCommandDocs.ForMode(mode, _hostname);

    /// <summary>The command table for whichever mode the CLI is currently in.</summary>
    public (string name, string desc)[] CurrentCommands
    {
        get
        {
            switch (_mode)
            {
                case Mode.UserExec:        return UserExecCommands;
                case Mode.PrivilegedExec:  return PrivilegedExecCommands;
                case Mode.GlobalConfig:    return _globalConfigCommands;
                case Mode.InterfaceConfig: return _interfaceConfigCommands;
                default:                   return UserExecCommands;
            }
        }
    }

    /// <summary>Distinct first-token keywords in the current mode whose name starts with prefix (case-insensitive).</summary>
    public List<string> MatchPrefix(string prefix)
    {
        var matches = new List<string>();
        if (string.IsNullOrEmpty(prefix)) return matches;

        foreach (var (name, _) in CurrentCommands)
        {
            string keyword = name.Split(' ')[0];
            if (keyword.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                !matches.Any(m => m.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
                matches.Add(keyword);
        }
        return matches;
    }

    /// <summary>True when the last Execute() call assigned an interface an IP
    /// address — lets the interactive panel react (CLIPanelManager's sound)
    /// without scripted setup (Level7SceneSetup) doing the same.</summary>
    public bool LastCommandAssignedAddress { get; private set; }

    /// <summary>
    /// Process one line of input.
    /// Returns null for silent success, "__CLOSE__" to close the panel,
    /// or an output string to display.
    /// </summary>
    public string Execute(string raw)
    {
        LastCommandAssignedAddress = false;
        string input = raw.Trim();
        if (input.Length == 0) return null;

        string[] p   = input.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
        string   cmd = p[0].ToLower();

        // Resolve an unambiguous first-token abbreviation to its full keyword
        // before dispatch — same candidate list Tab-complete already offers
        // via MatchPrefix (see HandleTabComplete in CLIPanelManager), just
        // resolved automatically on Enter instead of requiring Tab, mirroring
        // real IOS's "type enough to be unambiguous" behavior. Scope matches
        // Tab-complete's own: first token only, not sub-arguments.
        var abbrevMatches = MatchPrefix(cmd);
        if (abbrevMatches.Count == 1) cmd = abbrevMatches[0];
        else if (abbrevMatches.Count > 1) return $"% Ambiguous command: \"{p[0]}\"";

        switch (_mode)
        {
            case Mode.UserExec:       return ExecUser(cmd, p);
            case Mode.PrivilegedExec: return ExecPriv(cmd, p);
            case Mode.GlobalConfig:   return ExecGlobalCfg(cmd, p);
            case Mode.InterfaceConfig: return ExecInterfaceCfg(cmd, p);
            default:                  return "% Unknown mode";
        }
    }

    // ── User EXEC ─────────────────────────────────────────────────────────────

    string ExecUser(string cmd, string[] p)
    {
        switch (cmd)
        {
            case "enable":
                _mode = Mode.PrivilegedExec;
                return null;
            case "exit":
                return "__CLOSE__";
            case "?":
            case "help":
                return FormatHelp("User EXEC commands:", UserExecCommands);
            default:
                return NotRecognized(p[0]);
        }
    }

    // ── Privileged EXEC ───────────────────────────────────────────────────────

    string ExecPriv(string cmd, string[] p)
    {
        switch (cmd)
        {
            case "exit":
                return "__CLOSE__";
            case "configure":
                if (p.Length >= 2 && (p[1].ToLower() == "terminal" || p[1].ToLower() == "t"))
                {
                    _mode = Mode.GlobalConfig;
                    return "Enter configuration commands, one per line.  End with 'end'.";
                }
                return "% Usage: configure terminal";
            case "show":
                return ExecShow(p);
            case "?":
            case "help":
                return FormatHelp("Privileged EXEC commands:", PrivilegedExecCommands);
            default:
                return NotRecognized(p[0]);
        }
    }

    string ExecShow(string[] p)
    {
        if (p.Length < 2) return "% Incomplete command. Try 'show ?'";
        string sub = p[1].ToLower();

        switch (sub)
        {
            case "ip":
                if (p.Length >= 4 && p[2].ToLower() == "interface" && p[3].ToLower() == "brief")
                    return ShowIpInterfaceBrief();
                return "% Usage: show ip interface brief";
            case "?":
                return "show sub-commands:\n" +
                       "  ip interface brief   Show every port's IP address and status";
            default:
                return $"% Unknown show sub-command '{p[1]}'.";
        }
    }

    string ShowIpInterfaceBrief()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{"Interface",-10} {"IP-Address",-16} {"Status",-22}");
        for (int i = 0; i < _ifaceConfig.Count; i++)
        {
            var iface  = _ifaceConfig.Get(i);
            string ip     = string.IsNullOrEmpty(iface.ip) ? "unassigned" : iface.ip;
            string status = iface.shutdown ? "administratively down" : "up";
            sb.AppendLine($"{"eth" + i,-10} {ip,-16} {status,-22}");
        }
        return sb.ToString().TrimEnd('\n', '\r');
    }

    // ── Global Config ─────────────────────────────────────────────────────────

    string ExecGlobalCfg(string cmd, string[] p)
    {
        switch (cmd)
        {
            case "end":
            case "exit":
                _mode = Mode.PrivilegedExec;
                return null;
            case "interface":
                if (p.Length < 2) return $"% Usage: interface eth<0-{_device.PortCount - 1}>";
                return EnterInterface(p[1]);
            case "?":
            case "help":
                return FormatHelp("Global configuration commands:", _globalConfigCommands);
            default:
                return NotRecognized(p[0]);
        }
    }

    string EnterInterface(string token)
    {
        string t = token.ToLower();
        if (t.StartsWith("eth") && int.TryParse(t.Substring(3), out int n)
            && n >= 0 && n < _device.PortCount)
        {
            _currentPort = n;
            _mode = Mode.InterfaceConfig;
            return null;
        }
        return $"% Invalid interface name '{token}'.  Use eth0..eth{_device.PortCount - 1}.";
    }

    // ── Interface Config ──────────────────────────────────────────────────────

    string ExecInterfaceCfg(string cmd, string[] p)
    {
        RouterInterfaceConfig.PortConfig iface = _ifaceConfig.Get(_currentPort);

        switch (cmd)
        {
            case "ip":
                if (p.Length >= 2 && p[1].ToLower() == "address")
                {
                    if (p.Length < 4) return "% Usage: ip address <ip> <mask>";
                    if (!SubnetMath.IsDottedQuad(p[2]) || !SubnetMath.IsDottedQuad(p[3]))
                        return "% Invalid IP address or mask format.";
                    int maskPrefix = SubnetMath.MaskToPrefixLength(p[3]);
                    if (maskPrefix < 0)
                        return "% Invalid subnet mask.";
                    if (maskPrefix > SubnetMath.MaxUsablePrefix)
                        return $"% Mask too small: /{maskPrefix} leaves no address a device can actually hold — use /{SubnetMath.MaxUsablePrefix} ({SubnetMath.PrefixLengthToMask(SubnetMath.MaxUsablePrefix)}) or a shorter mask.";
                    if (maskPrefix < SubnetMath.MinUsablePrefix)
                        return $"% Bad mask: {p[3]} puts every address in one subnet — use at least /{SubnetMath.MinUsablePrefix} ({SubnetMath.PrefixLengthToMask(SubnetMath.MinUsablePrefix)}).";
                    if (!SubnetMath.IsHostAddress(p[2], p[3]))
                        return "% Invalid host address: that's this subnet's network or broadcast address.";
                    string conflict = AddressConflict.Find(p[2], _device, _currentPort);
                    if (conflict != null)
                        return $"% Address already in use by {conflict}.";
                    if (!SubnetMath.SameSubnet(iface.ip, iface.mask, p[2], p[3]))
                        iface.addressedAtTicks = System.DateTime.UtcNow.Ticks;
                    iface.ip   = p[2];
                    iface.mask = p[3];
                    PushInterfaceState();
                    LastCommandAssignedAddress = true;
                    return null;
                }
                return "% Usage: ip address <ip> <mask>";
            case "shutdown":
                iface.shutdown = true;
                PushInterfaceState();
                return null;
            case "no":
                if (p.Length >= 2 && p[1].ToLower() == "shutdown")
                {
                    iface.shutdown = false;
                    PushInterfaceState();
                    return null;
                }
                return "% Usage: no shutdown";
            case "end":
                _mode = Mode.PrivilegedExec;
                return null;
            case "exit":
                _mode = Mode.GlobalConfig;
                return null;
            case "interface":
                // Jump straight to another interface without exiting back to
                // global config first — matches real IOS, and is exactly the
                // move Level4Guide's stage 3 teaches (straight from eth0's
                // "no shutdown" into "interface eth1").
                if (p.Length < 2) return $"% Usage: interface eth<0-{_device.PortCount - 1}>";
                return EnterInterface(p[1]);
            case "?":
            case "help":
                return FormatHelp("Interface configuration commands:", _interfaceConfigCommands);
            default:
                return NotRecognized(p[0]);
        }
    }

    // Mirrors the current interface's up/down state onto the device port so
    // Cable can decide whether to show packet flow, without Cable knowing
    // anything about IOS-style interface config.
    void PushInterfaceState()
    {
        RouterInterfaceConfig.PortConfig iface = _ifaceConfig.Get(_currentPort);
        bool isUp = !string.IsNullOrEmpty(iface.ip) && !iface.shutdown;
        _device.SetPortActive(_currentPort, isUp);
        SubnetVisualizer.RefreshAll();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Both getters translate RouterInterfaceConfig's "" (unassigned) to null at
    // this API boundary so existing callers (ReachabilityService, Level3Guide)
    // keep their existing null-checks working unchanged — only the underlying
    // storage moved to a serializable component, not this contract.

    /// <summary>Configured IP for a port, or null if unassigned. Used by ReachabilityService.</summary>
    public string GetInterfaceIp(int port)
    {
        var iface = _ifaceConfig.Get(port);
        return iface != null && !string.IsNullOrEmpty(iface.ip) ? iface.ip : null;
    }

    /// <summary>Configured mask for a port, or null if unassigned. Used by ReachabilityService.</summary>
    public string GetInterfaceMask(int port)
    {
        var iface = _ifaceConfig.Get(port);
        return iface != null && !string.IsNullOrEmpty(iface.mask) ? iface.mask : null;
    }

    static string FormatHelp(string title, (string name, string desc)[] commands)
    {
        int width = 0;
        foreach (var (name, _) in commands) width = Math.Max(width, name.Length);

        var sb = new StringBuilder();
        sb.AppendLine(title);
        foreach (var (name, desc) in commands)
            sb.AppendLine($"  {name.PadRight(width + 2)}{desc}");
        return sb.ToString().TrimEnd('\n', '\r');
    }

    string NotRecognized(string token)
    {
        string suggestion = null;
        int    bestDist   = int.MaxValue;

        foreach (var (name, _) in CurrentCommands)
        {
            string keyword = name.Split(' ')[0];
            int dist = Levenshtein(token.ToLowerInvariant(), keyword.ToLowerInvariant());
            if (dist < bestDist)
            {
                bestDist   = dist;
                suggestion = keyword;
            }
        }

        // Only suggest when the edit distance is small relative to the token length,
        // to avoid nonsense matches on very short or wildly different input.
        if (suggestion != null && bestDist <= Math.Max(1, token.Length / 3))
            return $"% Unrecognized command '{token}'.  Did you mean '{suggestion}'?";

        return $"% Unrecognized command '{token}'.  Type '?' for help.";
    }

    static int Levenshtein(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[a.Length, b.Length];
    }
}
