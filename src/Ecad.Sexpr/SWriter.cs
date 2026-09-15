using System.Text;

namespace Ecad.Sexpr;

public static class SWriter
{
    /// <summary>
    /// Writes the document preserving original trivia. Nodes without trivia (created or moved in code)
    /// get KiCad-style layout: sub-lists on their own tab-indented line, atoms separated by one space.
    /// </summary>
    public static string Write(SDocument doc)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < doc.Roots.Count; i++)
        {
            var node = doc.Roots[i];
            sb.Append(node.LeadingTrivia ?? (i == 0 ? string.Empty : "\n"));
            WriteBody(sb, node, 0);
        }

        sb.Append(doc.TrailingTrivia);
        return sb.ToString();
    }

    /// <summary>Writes a single node (without its own leading trivia) at depth 0.</summary>
    public static string WriteNode(SNode node)
    {
        var sb = new StringBuilder();
        WriteBody(sb, node, 0);
        return sb.ToString();
    }

    /// <summary>Single-line form with one space between tokens and no preserved whitespace.</summary>
    public static string WriteCompact(SNode node)
    {
        var sb = new StringBuilder();
        WriteCompact(sb, node);
        return sb.ToString();
    }

    /// <summary>Re-lays out the whole document exactly as KiCad's own formatter would.</summary>
    public static string WritePrettified(SDocument doc, KicadFormatMode mode = KicadFormatMode.Normal)
    {
        var sb = new StringBuilder();
        foreach (var node in doc.Roots)
        {
            WriteCompact(sb, node);
        }

        return KicadPrettifier.Prettify(sb.ToString(), mode);
    }

    private static void WriteBody(StringBuilder sb, SNode node, int depth)
    {
        if (node is SAtom atom)
        {
            sb.Append(atom.Raw);
            return;
        }

        var list = (SList)node;
        sb.Append('(');
        for (int i = 0; i < list.Count; i++)
        {
            var child = list[i];
            sb.Append(child.LeadingTrivia ?? DefaultTrivia(child, i, depth + 1));
            WriteBody(sb, child, depth + 1);
        }

        if (list.CloseTrivia is { } close)
        {
            sb.Append(close);
        }
        else if (list.HasListChildren)
        {
            sb.Append('\n').Append('\t', depth);
        }

        sb.Append(')');
    }

    private static string DefaultTrivia(SNode child, int index, int depth)
    {
        if (index == 0)
        {
            return string.Empty;
        }

        return child is SList ? "\n" + new string('\t', depth) : " ";
    }

    private static void WriteCompact(StringBuilder sb, SNode node)
    {
        if (node is SAtom atom)
        {
            sb.Append(atom.Raw);
            return;
        }

        var list = (SList)node;
        sb.Append('(');
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            WriteCompact(sb, list[i]);
        }

        sb.Append(')');
    }
}
