namespace SonnetDB.Cli;

internal static class ConsoleTableFormatter
{
    public static void Write(TextWriter writer, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var widths = new int[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            widths[i] = headers[i].Length;
        }

        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        writer.WriteLine(string.Join(" | ", Pad(headers, widths)));
        writer.WriteLine(string.Join("-+-", widths.Select(static width => new string('-', width))));

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(" | ", Pad(row, widths)));
        }
    }

    private static IEnumerable<string> Pad(IReadOnlyList<string> values, IReadOnlyList<int> widths)
    {
        for (var i = 0; i < values.Count; i++)
        {
            yield return values[i].PadRight(widths[i]);
        }
    }
}
