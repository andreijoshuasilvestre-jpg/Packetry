using System;

/// <summary>
/// Minimal dotted-quad IPv4 subnet math — deliberately shallow (no VLSM /
/// subnet-borrowing arithmetic), just enough to teach "same subnet or not"
/// and translate between a dotted mask and its /N (CIDR) form.
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

    public static int MaskToPrefixLength(string mask)
    {
        if (!TryParse(mask, out uint value)) return -1;
        int count = 0;
        for (int i = 31; i >= 0; i--)
        {
            if ((value & (1u << i)) != 0) count++;
            else break;
        }
        return count;
    }

    public static string PrefixLengthToMask(int prefix)
    {
        prefix = Math.Clamp(prefix, 0, 32);
        uint value = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        return $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";
    }

    /// <summary>Usable host addresses for a /prefix block (network + broadcast excluded).</summary>
    public static int UsableHostCount(int prefix)
    {
        prefix = Math.Clamp(prefix, 0, 32);
        if (prefix >= 31) return 0; // no usable host range worth teaching at this size
        return (1 << (32 - prefix)) - 2;
    }
}
