using System.Collections.Generic;

/// <summary>
/// Assigns each subnet a color slot (see GameTheme.SlotColor) in the order that
/// subnet is first seen, rather than deriving a color from a hash of the
/// network address — so subnet colors follow the player's own Settings >
/// Subnet Colors palette (first subnet configured gets slot 0, second gets
/// slot 1, ...) the same way on every level. Reset once per level (see
/// GameBootstrap.Awake) so each level's subnets start counting from slot 0.
/// Keyed by Subnet (network address and mask), so two subnets sharing a
/// network address but not a mask get different colors.
///
/// Slots are released again once their subnet no longer exists anywhere in
/// the scene (see Retain, driven by SubnetVisualizer.RefreshAll) and a new
/// subnet takes the lowest free slot. Without that, every subnet a player
/// ever typed this level — a mistyped mask, an address they later changed —
/// kept its slot forever, so each new subnet landed on a later and later
/// slot (past the palette's customizable ones) instead of reusing the colors
/// the player actually picked.
/// </summary>
public static class SubnetColorRegistry
{
    // Index = slot, value = the subnet holding it, null = free.
    static readonly List<Subnet?> s_slots = new List<Subnet?>();

    public static void Reset() => s_slots.Clear();

    /// <summary>This subnet's color slot — its existing one if it already has one
    /// this level, otherwise the lowest free slot, assigned and remembered now.</summary>
    public static int SlotFor(Subnet subnet)
    {
        int existing = s_slots.IndexOf(subnet);
        if (existing >= 0) return existing;

        int free = s_slots.IndexOf(null);
        if (free >= 0) { s_slots[free] = subnet; return free; }

        s_slots.Add(subnet);
        return s_slots.Count - 1;
    }

    /// <summary>Frees every slot whose subnet isn't in liveSubnets. Subnets that are
    /// still live keep their slot, so nothing already on screen changes color.</summary>
    public static void Retain(HashSet<Subnet> liveSubnets)
    {
        for (int i = 0; i < s_slots.Count; i++)
            if (s_slots[i].HasValue && !liveSubnets.Contains(s_slots[i].Value))
                s_slots[i] = null;

        while (s_slots.Count > 0 && s_slots[s_slots.Count - 1] == null)
            s_slots.RemoveAt(s_slots.Count - 1);
    }

    /// <summary>The subnet currently holding this slot, if any — lets the Settings
    /// dialog label each palette row with the subnet it's coloring right now.</summary>
    public static bool TryGetSubnet(int slot, out Subnet subnet)
    {
        subnet = default;
        if (slot < 0 || slot >= s_slots.Count || !s_slots[slot].HasValue) return false;
        subnet = s_slots[slot].Value;
        return true;
    }
}
