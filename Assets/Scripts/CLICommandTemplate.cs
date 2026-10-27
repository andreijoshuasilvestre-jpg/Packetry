using System.Collections.Generic;
using System.Text;
using TMPro;

/// <summary>
/// Parses RouterCLI's command-table strings (e.g. "ip address <ip> <mask>",
/// "interface eth<N>") into literal/parameter segments so the CLI command-builder
/// UI can render an editable field per parameter and recompose a runnable command
/// line from whatever values the user filled in.
/// </summary>
public static class CLICommandTemplate
{
    public struct Segment
    {
        public bool   IsParam;
        public string Text;  // literal text, or parameter name when IsParam
        public bool   Glued; // true = no separating space before this segment
    }

    public static List<Segment> Parse(string name)
    {
        var segments = new List<Segment>();
        string[] tokens = name.Split(' ');

        foreach (string tok in tokens)
        {
            int lt = tok.IndexOf('<');
            if (lt < 0)
            {
                segments.Add(new Segment { IsParam = false, Text = tok, Glued = false });
                continue;
            }

            // A token like "eth<N>" has a literal prefix glued directly to its
            // parameter (no space) — only a token starting with '<' (e.g. "<ip>")
            // is a standalone parameter separated from its neighbors by a space.
            bool firstPartOfToken = true;
            if (lt > 0)
            {
                segments.Add(new Segment { IsParam = false, Text = tok.Substring(0, lt), Glued = false });
                firstPartOfToken = false;
            }

            int gt = tok.IndexOf('>', lt);
            string paramName = tok.Substring(lt + 1, gt - lt - 1);
            segments.Add(new Segment { IsParam = true, Text = paramName, Glued = !firstPartOfToken });

            if (gt + 1 < tok.Length)
                segments.Add(new Segment { IsParam = false, Text = tok.Substring(gt + 1), Glued = true });
        }

        return segments;
    }

    /// <summary>True once a parameter field holds a finished value of its kind,
    /// not just a start on one — a whole dotted-quad address for &lt;ip&gt;, a legal
    /// mask for &lt;mask&gt; (see SubnetMath.ClassifyMaskInput), a port number for
    /// &lt;N&gt;, anything at all for any other parameter. Lets a guide wait for the
    /// player to finish typing "192.168.1.1" before pointing at the next field,
    /// instead of moving on at the first keystroke.</summary>
    public static bool IsParamComplete(string param, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        switch (param)
        {
            case "ip":   return SubnetMath.IsDottedQuad(value);
            case "mask": return SubnetMath.ClassifyMaskInput(value) == SubnetMath.MaskInputState.Valid;
            case "N":    return int.TryParse(value, out _);
            default:     return true;
        }
    }

    public static string Compose(List<Segment> segments, Dictionary<string, TMP_InputField> paramFields)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            string text = seg.Text;
            if (seg.IsParam && paramFields.TryGetValue(seg.Text, out var field) && field != null)
                text = field.text;

            if (!seg.Glued && sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }
        return sb.ToString();
    }
}
