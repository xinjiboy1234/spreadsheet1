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

            var merges = ParseMerges(sheet);
            ScanSheet(sheet, sheetName, fields, schema, merges);
        }

        schema.Fields = fields.OrderBy(f => f, StringComparer.Ordinal).ToList();
        return schema;
    }

    private static void ScanSheet(
        JsonElement sheet,
        string sheetName,
        HashSet<string> fields,
        TemplateSchema schema,
        List<MergeRange> merges)
    {
        if (!sheet.TryGetProperty("cellData", out var cellData) || cellData.ValueKind != JsonValueKind.Object)
            return;

        var tokens = new List<Token>();

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

        var openStack = new Stack<(string Name, int Row)>();
        var openLoops = new Dictionary<string, OpenLoop>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            if (token.Prefix == "#")
            {
                if (openStack.Count > 0)
                {
                    schema.Warnings.Add(
                        $"Nested loop '{token.Name}' inside '{openStack.Peek().Name}' on sheet '{sheetName}' is not supported.");
                }

                openStack.Push((token.Name, token.Row));
                if (!openLoops.ContainsKey(token.Name))
                {
                    openLoops[token.Name] = new OpenLoop(token.Name, token.Row);
                }
                continue;
            }

            if (token.Prefix == "/")
            {
                if (!openLoops.TryGetValue(token.Name, out var open))
                {
                    schema.Warnings.Add(
                        $"Loop end '/{token.Name}' on sheet '{sheetName}' has no matching start.");
                    continue;
                }

                while (openStack.Count > 0 && openStack.Peek().Name != token.Name)
                    openStack.Pop();
                if (openStack.Count > 0 && openStack.Peek().Name == token.Name)
                    openStack.Pop();

                var loop = new TemplateLoop
                {
                    Name = token.Name,
                    Sheet = sheetName,
                    StartRow = open.StartRow,
                    EndRow = token.Row,
                    Fields = open.Fields.OrderBy(f => f, StringComparer.Ordinal).ToList()
                };

                if (loop.StartRow != loop.EndRow)
                {
                    schema.Warnings.Add(
                        $"Loop '{token.Name}' on sheet '{sheetName}' spans multiple rows (startRow={loop.StartRow}, endRow={loop.EndRow}); only single-row loops are supported.");
                }

                if (OverlapsMerge(loop.StartRow, loop.EndRow, merges))
                {
                    schema.Warnings.Add(
                        $"Loop '{token.Name}' on sheet '{sheetName}' overlaps a merge range.");
                }

                schema.Loops.Add(loop);
                openLoops.Remove(token.Name);
                continue;
            }

            // Simple field or loop field (Name.Prop)
            if (token.Property is not null)
            {
                if (openLoops.TryGetValue(token.Name, out var open))
                {
                    open.Fields.Add(token.Property);
                }
                else
                {
                    // Dotted reference outside an open loop — treat as loop field orphan or ignore top-level
                    // Spec: {{ListName.Field}} is loop-internal; if no open loop, still don't add to top-level fields
                }
                continue;
            }

            fields.Add(token.Name);
        }

        foreach (var orphan in openLoops.Values)
        {
            schema.Warnings.Add(
                $"Loop start '#{orphan.Name}' on sheet '{sheetName}' has no matching end.");
        }
    }

    private static bool OverlapsMerge(int startRow, int endRow, List<MergeRange> merges)
    {
        foreach (var m in merges)
        {
            if (startRow <= m.EndRow && endRow >= m.StartRow)
                return true;
        }
        return false;
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
            merges.Add(new MergeRange(sr, er));
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

    private sealed class OpenLoop(string name, int startRow)
    {
        public string Name { get; } = name;
        public int StartRow { get; } = startRow;
        public HashSet<string> Fields { get; } = new(StringComparer.Ordinal);
    }

    private sealed record MergeRange(int StartRow, int EndRow);
}
