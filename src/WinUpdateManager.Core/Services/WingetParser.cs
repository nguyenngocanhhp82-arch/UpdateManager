using System.Text.RegularExpressions;
using WinUpdateManager.Core.Models;

namespace WinUpdateManager.Core.Services;

public static partial class WingetParser
{
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex AnsiEscapeRegex();

    [GeneratedRegex(@"\b(\d+)\s+(?:upgrades? available|packages? have version|bản nâng cấp)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SummaryFooterRegex();

    public record ColumnRange(string Name, int StartIndex, int? Length);

    public static IReadOnlyList<WingetPackage> ParseTable(string rawOutput, bool hasAvailableColumn = true)
    {
        var result = new List<WingetPackage>();
        if (string.IsNullOrWhiteSpace(rawOutput)) return result;

        var lines = NormalizeLines(rawOutput);
        int separatorIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            // A separator line contains predominantly '-' (at least 10 dashes)
            if (line.Count(c => c == '-') >= 10 && (line.Replace("-", "").Replace(" ", "").Length == 0))
            {
                separatorIndex = i;
                break;
            }
        }

        if (separatorIndex <= 0)
        {
            return result;
        }

        string headerLine = lines[separatorIndex - 1];
        string separatorLine = lines[separatorIndex];

        var columns = DetectColumns(headerLine, separatorLine, hasAvailableColumn);
        if (columns.Count < 3)
        {
            return result;
        }

        for (int i = separatorIndex + 1; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) break;

            if (SummaryFooterRegex().IsMatch(line) || line.StartsWith("No installed package", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var package = ExtractPackageFromLine(line, columns, hasAvailableColumn);
            if (package != null && !string.IsNullOrWhiteSpace(package.Id) && !string.IsNullOrWhiteSpace(package.Name))
            {
                result.Add(package);
            }
        }

        return result;
    }

    public static HashSet<string> ParsePinnedIds(string rawOutput)
    {
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = ParseTable(rawOutput, hasAvailableColumn: false);
        foreach (var p in packages)
        {
            if (!string.IsNullOrWhiteSpace(p.Id))
            {
                pinned.Add(p.Id);
            }
        }
        return pinned;
    }

    public static IReadOnlyList<string> ParseVersions(string rawOutput)
    {
        var versions = new List<string>();
        if (string.IsNullOrWhiteSpace(rawOutput)) return versions;

        var lines = NormalizeLines(rawOutput);
        bool foundHeader = false;
        bool foundSeparator = false;

        foreach (var line in lines)
        {
            if (!foundHeader)
            {
                if (line.StartsWith("Version", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Phiên bản", StringComparison.OrdinalIgnoreCase))
                {
                    foundHeader = true;
                }
                continue;
            }

            if (!foundSeparator)
            {
                if (line.Contains("---"))
                {
                    foundSeparator = true;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) break;
            string ver = line.Trim();
            if (!string.IsNullOrWhiteSpace(ver) && !ver.StartsWith('-'))
            {
                versions.Add(ver);
            }
        }

        return versions;
    }

    private static List<ColumnRange> DetectColumns(string headerLine, string separatorLine, bool hasAvailableColumn)
    {
        var columns = new List<ColumnRange>();

        // Method 1: If separator line has spaces between dash blocks: e.g. "----------  ------"
        if (separatorLine.Contains("  "))
        {
            var parts = Regex.Matches(separatorLine, @"-+");
            for (int i = 0; i < parts.Count; i++)
            {
                var match = parts[i];
                int start = match.Index;
                int? length = (i < parts.Count - 1) ? parts[i + 1].Index - start : null;
                string colName = SafeSubstring(headerLine, start, length).Trim();
                columns.Add(new ColumnRange(colName, start, length));
            }
            return columns;
        }

        // Method 2: Separator line is continuous dashes. Find word boundaries in header
        var wordMatches = Regex.Matches(headerLine, @"\S+");
        if (wordMatches.Count >= 3)
        {
            for (int i = 0; i < wordMatches.Count; i++)
            {
                int start = wordMatches[i].Index;
                int? length = (i < wordMatches.Count - 1) ? wordMatches[i + 1].Index - start : null;
                string name = wordMatches[i].Value;
                columns.Add(new ColumnRange(name, start, length));
            }
            return columns;
        }

        return columns;
    }

    private static WingetPackage? ExtractPackageFromLine(string line, List<ColumnRange> columns, bool hasAvailableColumn)
    {
        try
        {
            string name = string.Empty;
            string id = string.Empty;
            string version = string.Empty;
            string available = string.Empty;
            string source = string.Empty;

            // Map by column index
            if (columns.Count >= 5 && hasAvailableColumn)
            {
                name = SafeSubstring(line, columns[0].StartIndex, columns[0].Length).Trim();
                id = SafeSubstring(line, columns[1].StartIndex, columns[1].Length).Trim();
                version = SafeSubstring(line, columns[2].StartIndex, columns[2].Length).Trim();
                available = SafeSubstring(line, columns[3].StartIndex, columns[3].Length).Trim();
                source = SafeSubstring(line, columns[4].StartIndex, columns[4].Length).Trim();
            }
            else if (columns.Count >= 4)
            {
                name = SafeSubstring(line, columns[0].StartIndex, columns[0].Length).Trim();
                id = SafeSubstring(line, columns[1].StartIndex, columns[1].Length).Trim();
                version = SafeSubstring(line, columns[2].StartIndex, columns[2].Length).Trim();
                if (hasAvailableColumn)
                {
                    available = SafeSubstring(line, columns[3].StartIndex, columns[3].Length).Trim();
                }
                else
                {
                    source = SafeSubstring(line, columns[3].StartIndex, columns[3].Length).Trim();
                }
            }
            else if (columns.Count == 3)
            {
                name = SafeSubstring(line, columns[0].StartIndex, columns[0].Length).Trim();
                id = SafeSubstring(line, columns[1].StartIndex, columns[1].Length).Trim();
                version = SafeSubstring(line, columns[2].StartIndex, columns[2].Length).Trim();
            }

            return new WingetPackage
            {
                Name = name,
                Id = id,
                CurrentVersion = version,
                AvailableVersion = available,
                Source = source
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> NormalizeLines(string rawOutput)
    {
        var result = new List<string>();
        string cleaned = AnsiEscapeRegex().Replace(rawOutput, string.Empty);
        var rawLines = cleaned.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var rawLine in rawLines)
        {
            string line = rawLine;
            // Handle carriage return: take content after last \r
            if (line.Contains('\r'))
            {
                string[] parts = line.Split('\r', StringSplitOptions.RemoveEmptyEntries);
                line = parts.Length > 0 ? parts[^1] : string.Empty;
            }

            result.Add(line.TrimEnd());
        }

        return result;
    }

    private static string SafeSubstring(string text, int startIndex, int? length)
    {
        if (startIndex >= text.Length) return string.Empty;
        if (!length.HasValue) return text.Substring(startIndex);

        int maxLen = Math.Min(length.Value, text.Length - startIndex);
        return maxLen > 0 ? text.Substring(startIndex, maxLen) : string.Empty;
    }
}
