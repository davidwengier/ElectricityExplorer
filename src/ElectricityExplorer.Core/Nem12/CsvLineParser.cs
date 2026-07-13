using System.Text;

namespace ElectricityExplorer.Core.Nem12;

internal static class CsvLineParser
{
    public static IReadOnlyList<string> Parse(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }

                continue;
            }

            if (character == ',' && !insideQuotes)
            {
                fields.Add(value.ToString());
                value.Clear();
                continue;
            }

            value.Append(character);
        }

        if (insideQuotes)
        {
            throw new FormatException("The CSV record contains an unterminated quoted field.");
        }

        fields.Add(value.ToString());
        return fields;
    }
}
