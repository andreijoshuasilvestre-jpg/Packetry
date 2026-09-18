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
