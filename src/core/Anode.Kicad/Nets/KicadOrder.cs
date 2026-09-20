namespace Anode.Kicad;

/// <summary>
/// KiCad's own ordering of names (<c>StrNumCmp</c>): digits inside a name compare as numbers, so R2 comes before
/// R10 and /D2 before /D10. What orders the nets of a netlist and the parts of a bill of materials.
/// </summary>
internal sealed class KicadOrder : IComparer<string>
{
    public static readonly KicadOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        ReadOnlySpan<char> a = x ?? string.Empty, b = y ?? string.Empty;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int ia = i, jb = j;
                while (i < a.Length && char.IsDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsDigit(b[j]))
                {
                    j++;
                }

                var left = a[ia..i].TrimStart('0');
                var right = b[jb..j].TrimStart('0');
                if (left.Length != right.Length)
                {
                    return left.Length - right.Length;
                }

                int digits = left.SequenceCompareTo(right);
                if (digits != 0)
                {
                    return digits;
                }

                continue;
            }

            if (a[i] != b[j])
            {
                return a[i] - b[j];
            }

            i++;
            j++;
        }

        return (a.Length - i) - (b.Length - j);
    }
}
