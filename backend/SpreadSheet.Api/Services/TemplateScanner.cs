using System.Text.Json;
using System.Text.RegularExpressions;
using SpreadSheet.Api.Models;

namespace SpreadSheet.Api.Services;

public class TemplateScanner
{
    private static readonly Regex PlaceholderRegex = new(
        @"\{\{\s*([#/]?)([A-Za-z_][A-Za-z0-9_]*)(?:\.([A-Za-z_][A-Za-z0-9_]*))?\s*\}\}",
        RegexOptions.Compiled);

    public TemplateSchema Scan(string workbookJson)
    {
        var schema = new TemplateSchema();
        if (string.IsNullOrWhiteSpace(workbookJson))
            return schema;

        using var doc = JsonDocument.Parse(workbookJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("sheets", out var sheets) || sheets.ValueKind != JsonValueKind.Object)
            return schema;

        var fields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sheetProp in sheets.EnumerateObject())
        {
            var sheet = sheetProp.Value;
            var sheetName = sheet.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? sheetProp.Name
                : sheetProp.Name;

            ScanSheet(sheet, sheetName, fields, schema);
        }

        schema.Fields = fields.OrderBy(f => f, StringComparer.Ordinal).ToList();
        return schema;
    }

    private static void ScanSheet(
        JsonElement sheet,
        string sheetName,
        HashSet<string> fields,
        TemplateSchema schema)
    {
        if (!sheet.TryGetProperty("cellData", out var cellData) || cellData.ValueKind != JsonValueKind.Object)
            return;

        var tokens = new List<Token>();
        var formulaRows = new HashSet<int>();

        foreach (var rowProp in cellData.EnumerateObject())
        {
            if (!int.TryParse(rowProp.Name, out var row))
                continue;
            if (rowProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var colProp in rowProp.Value.EnumerateObject())
            {
                if (!int.TryParse(colProp.Name, out var col))
                    continue;
                if (colProp.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (colProp.Value.TryGetProperty("f", out var fEl) &&
                    fEl.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(fEl.GetString()))
                {
                    formulaRows.Add(row);
                }

                if (!colProp.Value.TryGetProperty("v", out var vEl))
                    continue;

                var text = vEl.ValueKind switch
                {
                    JsonValueKind.String => vEl.GetString() ?? string.Empty,
                    JsonValueKind.Number => vEl.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(text))
                    continue;

                foreach (Match match in PlaceholderRegex.Matches(text))
                {
                    tokens.Add(new Token(
                        row,
                        col,
                        match.Groups[1].Value,
                        match.Groups[2].Value,
                        match.Groups[3].Success ? match.Groups[3].Value : null,
                        match.Index));
                }
            }
        }

        tokens.Sort((a, b) =>
        {
            var c = a.Row.CompareTo(b.Row);
            if (c != 0) return c;
            c = a.Col.CompareTo(b.Col);
            if (c != 0) return c;
            return a.Index.CompareTo(b.Index);
        });

        var loops = new List<TemplateLoop>();
        var openStack = new List<OpenLoop>();
        var registered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            if (token.Prefix == "#")
            {
                var duplicate = registered.Contains(token.Name) ||
                    openStack.Any(o => string.Equals(o.Name, token.Name, StringComparison.Ordinal));

                if (duplicate)
                {
                    schema.Warnings.Add(
                        $"Duplicate loop name '{token.Name}' on sheet '{sheetName}'; the later block is ignored.");
                }

                openStack.Add(new OpenLoop(
                    token.Name,
                    token.Row,
                    openStack.Count > 0 ? openStack[^1].Name : null,
                    openStack.Count,
                    duplicate));
                continue;
            }

            if (token.Prefix == "/")
            {
                var idx = openStack.FindLastIndex(o => string.Equals(o.Name, token.Name, StringComparison.Ordinal));
                if (idx < 0)
                {
                    schema.Warnings.Add(
                        $"Loop end '/{token.Name}' on sheet '{sheetName}' has no matching start.");
                    continue;
                }

                // Anything still open above this loop crosses its boundary instead of nesting inside it.
                for (var i = openStack.Count - 1; i > idx; i--)
                {
                    schema.Warnings.Add(
                        $"Loop '{openStack[i].Name}' on sheet '{sheetName}' crosses loop '{token.Name}' and is ignored.");
                    openStack.RemoveAt(i);
                }

                var open = openStack[idx];
                openStack.RemoveAt(idx);

                if (!open.Ignored)
                {
                    loops.Add(new TemplateLoop
                    {
                        Name = open.Name,
                        Sheet = sheetName,
                        StartRow = open.StartRow,
                        EndRow = token.Row,
                        Fields = open.Fields.OrderBy(f => f, StringComparer.Ordinal).ToList(),
                        ParentName = open.ParentName,
                        Depth = open.Depth
                    });
                    registered.Add(open.Name);
                }

                continue;
            }

            if (token.Property is not null)
            {
                var owner = openStack.FindLast(o => string.Equals(o.Name, token.Name, StringComparison.Ordinal));
                owner?.Fields.Add(token.Property);
                continue;
            }

            fields.Add(token.Name);
        }

        foreach (var orphan in openStack)
        {
            schema.Warnings.Add(
                $"Loop start '#{orphan.Name}' on sheet '{sheetName}' has no matching end.");
        }

        schema.Loops.AddRange(loops);

        AddFormulaWarnings(loops, formulaRows, sheetName, schema);
        AddMergeBoundaryWarnings(loops, ParseMerges(sheet), sheetName, schema);
        AddUnmappedFeatureWarnings(sheet, loops, sheetName, schema);
    }

    private static void AddFormulaWarnings(
        List<TemplateLoop> loops,
        HashSet<int> formulaRows,
        string sheetName,
        TemplateSchema schema)
    {
        if (formulaRows.Count == 0)
            return;

        foreach (var loop in loops)
        {
            if (formulaRows.Any(r => r >= loop.StartRow && r <= loop.EndRow))
            {
                schema.Warnings.Add(
                    $"Loop '{loop.Name}' on sheet '{sheetName}' contains formula cells; row references are not rewritten when the block expands.");
            }
        }
    }

    private static void AddMergeBoundaryWarnings(
        List<TemplateLoop> loops,
        List<MergeRange> merges,
        string sheetName,
        TemplateSchema schema)
    {
        if (loops.Count == 0)
            return;

        foreach (var m in merges)
        {
            if (FindInnermostContaining(loops, m.StartRow, m.EndRow) is not null)
                continue;
            if (!loops.Any(l => m.StartRow <= l.EndRow && m.EndRow >= l.StartRow))
                continue;

            schema.Warnings.Add(
                $"Merge range rows {m.StartRow}-{m.EndRow} columns {m.StartColumn}-{m.EndColumn} on sheet '{sheetName}' crosses a loop boundary; it will be dropped when filling.");
        }
    }

    private static void AddUnmappedFeatureWarnings(
        JsonElement sheet,
        List<TemplateLoop> loops,
        string sheetName,
        TemplateSchema schema)
    {
        if (loops.Count == 0)
            return;

        if (HasArrayItems(sheet, "hyperLink"))
        {
            schema.Warnings.Add(
                $"Sheet '{sheetName}' has hyperlinks; their row references are not remapped when loops expand.");
        }

        if (HasArrayItems(sheet, "arrayFormulas"))
        {
            schema.Warnings.Add(
                $"Sheet '{sheetName}' has array formulas; their row references are not remapped when loops expand.");
        }
    }

    private static bool HasArrayItems(JsonElement sheet, string name) =>
        sheet.TryGetProperty(name, out var el) &&
        el.ValueKind == JsonValueKind.Array &&
        el.GetArrayLength() > 0;

    /// <summary>Innermost loop whose row range fully contains [startRow, endRow], or null.</summary>
    internal static TemplateLoop? FindInnermostContaining(
        IReadOnlyList<TemplateLoop> loops,
        int startRow,
        int endRow)
    {
        TemplateLoop? best = null;
        foreach (var l in loops)
        {
            if (l.StartRow > startRow || l.EndRow < endRow)
                continue;
            if (best is null || l.EndRow - l.StartRow < best.EndRow - best.StartRow)
                best = l;
        }
        return best;
    }

    private static List<MergeRange> ParseMerges(JsonElement sheet)
    {
        var merges = new List<MergeRange>();
        if (!sheet.TryGetProperty("mergeData", out var mergeData) || mergeData.ValueKind != JsonValueKind.Array)
            return merges;

        foreach (var item in mergeData.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            if (!TryGetInt(item, "startRow", out var sr) ||
                !TryGetInt(item, "endRow", out var er))
                continue;
            TryGetInt(item, "startColumn", out var sc);
            TryGetInt(item, "endColumn", out var ec);
            merges.Add(new MergeRange(sr, er, sc, ec));
        }

        return merges;
    }

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
            return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value))
            return true;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value))
            return true;
        return false;
    }

    private sealed record Token(int Row, int Col, string Prefix, string Name, string? Property, int Index);

    private sealed class OpenLoop(string name, int startRow, string? parentName, int depth, bool ignored)
    {
        public string Name { get; } = name;
        public int StartRow { get; } = startRow;
        public string? ParentName { get; } = parentName;
        public int Depth { get; } = depth;
        public bool Ignored { get; } = ignored;
        public HashSet<string> Fields { get; } = new(StringComparer.Ordinal);
    }

    private sealed record MergeRange(int StartRow, int EndRow, int StartColumn, int EndColumn);
}
