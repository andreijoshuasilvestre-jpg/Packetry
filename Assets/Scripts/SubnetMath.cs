using System;

/// <summary>
/// A subnet's identity: its network address AND its mask — the same pair
/// SubnetMath.SameSubnet (and so ping) compares. The network address alone
/// isn't enough: 192.168.1.5/27 and 192.168.1.6/28 share 192.168.1.0 but are
/// different subnets that can't ping each other, and colors/the PC monitor's
/// warning keyed on the network address alone showed them as matching.
/// </summary>
public readonly struct Subnet : IEquatable<Subnet>
{
    public readonly uint Network;
    public readonly uint Mask;

    public Subnet(uint network, uint mask)
    {
        Network = network;
        Mask    = mask;
    }

    public bool Equals(Subnet other) => Network == other.Network && Mask == other.Mask;
    public override bool Equals(object obj) => obj is Subnet other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Network, Mask);
    public static bool operator ==(Subnet a, Subnet b) => a.Equals(b);
    public static bool operator !=(Subnet a, Subnet b) => !a.Equals(b);

    /// <summary>"192.168.1.0/24" — or dotted mask form for a non-contiguous mask,
    /// which nothing in-game can produce but a hand-edited save could.</summary>
    public override string ToString()
    {
        string network = SubnetMath.ToDottedQuad(Network);
        string mask    = SubnetMath.ToDottedQuad(Mask);
        int prefix     = SubnetMath.MaskToPrefixLength(mask);
        return prefix >= 0 ? $"{network}/{prefix}" : $"{network} {mask}";
    }
}

/// <summary>
/// Minimal dotted-quad IPv4 subnet math — deliberately shallow (no automatic
/// VLSM solver: nothing here splits a block into pieces for the player).
/// RangesOverlap/IsWithinSupernet are the one exception, added for Level11
/// Challenge's VLSM level — they only ever check a player-chosen allocation,
/// never compute one.
/// </summary>
public static class SubnetMath
{
    public static bool IsDottedQuad(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        string[] parts = s.Split('.');
        if (parts.Length != 4) return false;
        foreach (string part in parts)
            if (!byte.TryParse(part, out _)) return false;
        return true;
    }

    public static bool TryParse(string dotted, out uint value)
    {
        value = 0;
        if (!IsDottedQuad(dotted)) return false;
        string[] parts = dotted.Split('.');
        value = ((uint)byte.Parse(parts[0]) << 24) | ((uint)byte.Parse(parts[1]) << 16) |
                ((uint)byte.Parse(parts[2]) << 8)  |  (uint)byte.Parse(parts[3]);
        return true;
    }

    public static uint NetworkAddress(string ip, string mask)
    {
        TryParse(ip, out uint ipVal);
        TryParse(mask, out uint maskVal);
        return ipVal & maskVal;
    }

    /// <summary>The subnet ip/mask belongs to — network address and mask together,
    /// see Subnet.</summary>
    public static Subnet SubnetOf(string ip, string mask)
    {
        TryParse(mask, out uint maskVal);
        return new Subnet(NetworkAddress(ip, mask), maskVal);
    }

    /// <summary>True only if both masks match and both IPs land on the same network address under it.</summary>
    public static bool SameSubnet(string ip1, string mask1, string ip2, string mask2)
    {
        if (!IsDottedQuad(ip1) || !IsDottedQuad(mask1) || !IsDottedQuad(ip2) || !IsDottedQuad(mask2))
            return false;
        TryParse(mask1, out uint m1);
        TryParse(mask2, out uint m2);
        if (m1 != m2) return false;
        return NetworkAddress(ip1, mask1) == NetworkAddress(ip2, mask2);
    }

    /// <summary>Prefix length for a mask, or -1 if the mask isn't a valid contiguous
    /// left-aligned run of 1-bits (e.g. 255.255.255.10) — a malformed mask must never
    /// silently resolve to whatever prefix its leading 1-bits happen to spell out.</summary>
    public static int MaskToPrefixLength(string mask)
    {
        if (!TryParse(mask, out uint value)) return -1;
        int count = 0;
        int i = 31;
        for (; i >= 0; i--)
        {
            if ((value & (1u << i)) != 0) count++;
            else break;
        }
        for (; i >= 0; i--)
            if ((value & (1u << i)) != 0) return -1; // a 1-bit after the first 0 — not contiguous
        return count;
    }

    /// <summary>Largest prefix length this game treats as an assignable subnet. /31 and
    /// /32 are mathematically valid masks (and real /31 point-to-point links exist per
    /// RFC 3021), but this game implements no such exception — every subnet always needs
    /// at least one address a PC or router interface can actually hold, which /31 and /32
    /// never leave (see UsableHostCount, always 0 at that size). Below the minimum a level
    /// ever asks a subnet to hold, so they're excluded outright rather than offered as a
    /// technically-legal-but-always-rejected trap.</summary>
    public const int MaxUsablePrefix = 30;

    /// <summary>Smallest prefix length this game treats as an assignable subnet. Every
    /// contiguous mask from 128.0.0.0 (/1) up is a real subnet mask — the old classful
    /// "nothing below 255.0.0.0 (/8)" rule stopped applying once CIDR replaced address
    /// classes — but 0.0.0.0 (/0) puts every IPv4 address in one subnet, so real IOS
    /// rejects it on an interface ("Bad mask") and so does this game.</summary>
    public const int MinUsablePrefix = 1;

    /// <summary>All valid dotted-quad masks from MinUsablePrefix through MaxUsablePrefix,
    /// used to judge whether a partially-typed mask could still become valid (see
    /// ClassifyMaskInput). /0, /31 and /32 are deliberately excluded — see
    /// MinUsablePrefix/MaxUsablePrefix.</summary>
    static readonly string[] ValidMasks = BuildValidMasks();
    static string[] BuildValidMasks()
    {
        var masks = new string[MaxUsablePrefix - MinUsablePrefix + 1];
        for (int p = MinUsablePrefix; p <= MaxUsablePrefix; p++) masks[p - MinUsablePrefix] = PrefixLengthToMask(p);
        return masks;
    }

    public enum MaskInputState { Empty, PossiblyValid, Valid, Invalid }

    /// <summary>Classifies a subnet mask as the player is still typing it, character by
    /// character — Valid once it exactly matches one of the legal masks (MinUsablePrefix..
    /// MaxUsablePrefix), PossiblyValid while it's still a string-prefix of at least one of
    /// them, Invalid the moment it diverges from all of them — including finishing on a
    /// /0, /31 or /32, which can never complete into an assignable mask in this game. Used
    /// to give live feedback before the player commits with Enter.</summary>
    public static MaskInputState ClassifyMaskInput(string partial)
    {
        if (string.IsNullOrEmpty(partial)) return MaskInputState.Empty;
        bool anyPrefix = false;
        foreach (string candidate in ValidMasks)
        {
            if (candidate == partial) return MaskInputState.Valid;
            if (candidate.StartsWith(partial, StringComparison.Ordinal)) anyPrefix = true;
        }
        return anyPrefix ? MaskInputState.PossiblyValid : MaskInputState.Invalid;
    }

    public static string PrefixLengthToMask(int prefix)
    {
        prefix = Math.Clamp(prefix, 0, 32);
        uint value = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        return ToDottedQuad(value);
    }

    public static string ToDottedQuad(uint value) =>
        $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";

    /// <summary>Usable host addresses for a /prefix block (network + broadcast excluded).</summary>
    public static int UsableHostCount(int prefix)
    {
        prefix = Math.Clamp(prefix, 0, 32);
        if (prefix >= 31) return 0; // no usable host range worth teaching at this size
        // Computed in long: 1 << 31 (a /1) overflows int. /0's 2^32 - 2 doesn't fit an
        // int at all — it's never assignable anyway (see MinUsablePrefix), so it clamps.
        return (int)Math.Min(int.MaxValue, (1L << (32 - prefix)) - 2);
    }

    /// <summary>UsableHostCount with thousands separators ("2,147,483,646") — a /1's
    /// nine-digit count is unreadable as a bare number.</summary>
    public static string FormatHostCount(int prefix) =>
        UsableHostCount(prefix).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    public static uint BroadcastAddress(string ip, string mask)
    {
        TryParse(ip, out uint ipVal);
        TryParse(mask, out uint maskVal);
        return (ipVal & maskVal) | ~maskVal;
    }

    /// <summary>False if ip is the network or broadcast address of its own mask (e.g.
    /// 192.168.2.0 or 192.168.2.255 for a /24) — neither is ever assignable to a real
    /// PC or router interface, matching the "usable hosts" count already shown to the
    /// player (UsableHostCount above already excludes both from that number).</summary>
    public static bool IsHostAddress(string ip, string mask)
    {
        if (!IsDottedQuad(ip) || !IsDottedQuad(mask)) return false;
        TryParse(ip, out uint ipVal);
        return ipVal != NetworkAddress(ip, mask) && ipVal != BroadcastAddress(ip, mask);
    }

    /// <summary>True if the two subnets' address ranges (network..broadcast) share
    /// any address — the check a VLSM allocation's "no two pieces may overlap"
    /// requirement actually needs. Independent of SameSubnet, which only cares
    /// about exact equality: two differently-sized subnets can still overlap
    /// (e.g. a /25 that fully contains a /27).</summary>
    public static bool RangesOverlap(string ip1, string mask1, string ip2, string mask2)
    {
        if (!IsDottedQuad(ip1) || !IsDottedQuad(mask1) || !IsDottedQuad(ip2) || !IsDottedQuad(mask2))
            return false;
        uint aLo = NetworkAddress(ip1, mask1), aHi = BroadcastAddress(ip1, mask1);
        uint bLo = NetworkAddress(ip2, mask2), bHi = BroadcastAddress(ip2, mask2);
        return aLo <= bHi && bLo <= aHi;
    }

    /// <summary>True if subnet (ip, mask)'s entire address range falls within
    /// supernet (superIp, superMask)'s range — confirms a VLSM allocation
    /// actually came out of the shared block it was supposed to be carved
    /// from, rather than just happening to avoid overlapping the other pieces.</summary>
    public static bool IsWithinSupernet(string ip, string mask, string superIp, string superMask)
    {
        if (!IsDottedQuad(ip) || !IsDottedQuad(mask) || !IsDottedQuad(superIp) || !IsDottedQuad(superMask))
            return false;
        uint lo = NetworkAddress(ip, mask), hi = BroadcastAddress(ip, mask);
        uint superLo = NetworkAddress(superIp, superMask), superHi = BroadcastAddress(superIp, superMask);
        return lo >= superLo && hi <= superHi;
    }
}
