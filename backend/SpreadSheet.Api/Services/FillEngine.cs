using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpreadSheet.Api.Models;

namespace SpreadSheet.Api.Services;

public class FillResult
{
    public string WorkbookJson { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
}

public class FillEngine
{
    private static readonly Regex PlaceholderRegex = new(
        @"\{\{\s*([#/]?)([A-Za-z_][A-Za-z0-9_]*)(?:\.([A-Za-z_][A-Za-z0-9_]*))?\s*\}\}",
        RegexOptions.Compiled);

    public FillResult Fill(string workbookJson, JsonElement data, TemplateSchema schema)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(workbookJson))
            return new FillResult { WorkbookJson = workbookJson ?? string.Empty, Warnings = warnings };

        var root = JsonNode.Parse(workbookJson)!.AsObject();
        if (root["sheets"] is not JsonObject sheets)
            return new FillResult { WorkbookJson = root.ToJsonString(), Warnings = warnings };

        var loops = schema.Loops
            .OrderByDescending(l => l.StartRow)
            .ThenByDescending(l => l.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var loop in loops)
        {
            if (ShouldSkipLoop(loop, schema, warnings))
                continue;

            if (!TryFindSheet(sheets, loop.Sheet, out var sheetObj) || sheetObj is null)
            {
                warnings.Add($"Loop '{loop.Name}' sheet '{loop.Sheet}' not found; skipped.");
                continue;
            }

            var cellData = EnsureCellData(sheetObj);
            ExpandLoop(cellData, loop, data, warnings);
        }

        ReplaceSimpleFields(sheets, data, warnings);
        StripLoopMarkers(sheets);

        return new FillResult
        {
            WorkbookJson = root.ToJsonString(),
            Warnings = warnings
        };
    }

    private static bool ShouldSkipLoop(TemplateLoop loop, TemplateSchema schema, List<string> warnings)
    {
        if (loop.StartRow != loop.EndRow)
        {
            warnings.Add(
                $"Loop '{loop.Name}' spans multiple rows (startRow={loop.StartRow}, endRow={loop.EndRow}); skipped.");
            return true;
        }

        var schemaWarning = schema.Warnings.FirstOrDefault(w =>
            w.Contains(loop.Name, StringComparison.Ordinal) &&
            (ContainsAny(w, "merge", "合并", "nest", "嵌套")));

        if (schemaWarning is not null)
        {
            var reason = ContainsAny(schemaWarning, "merge", "合并") ? "merge conflict"
                : "nested loop";
            warnings.Add($"Loop '{loop.Name}' skipped due to {reason}.");
            return true;
        }

        // Also skip if any schema warning mentions nested involving this loop as outer/inner by name patterns
        // already covered by Contains(loop.Name) above for Outer in nested warning.

        return false;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static bool TryFindSheet(JsonObject sheets, string sheetName, out JsonObject? sheet)
    {
        foreach (var prop in sheets)
        {
            if (prop.Value is not JsonObject obj)
                continue;
            var name = obj["name"]?.GetValue<string>() ?? prop.Key;
            if (string.Equals(name, sheetName, StringComparison.Ordinal))
            {
                sheet = obj;
                return true;
            }
        }

        sheet = null;
        return false;
    }

    private static JsonObject EnsureCellData(JsonObject sheet)
    {
        if (sheet["cellData"] is JsonObject existing)
            return existing;
        var created = new JsonObject();
        sheet["cellData"] = created;
        return created;
    }

    private static void ExpandLoop(
        JsonObject cellData,
        TemplateLoop loop,
        JsonElement data,
        List<string> warnings)
    {
        var rowIndex = loop.StartRow;
        var items = GetArrayItems(data, loop.Name);

        if (items.Count == 0)
        {
            DeleteRow(cellData, rowIndex);
            return;
        }

        // Snapshot template before filling so extra rows keep placeholders
        var templateClone = cellData[rowIndex.ToString()]?.DeepClone();

        // Insert N-1 slots below template, shifting lower rows down first
        if (items.Count > 1)
            InsertRowsBelow(cellData, rowIndex, items.Count - 1);

        for (var i = 0; i < items.Count; i++)
        {
            var targetRow = rowIndex + i;
            if (i > 0 && templateClone is not null)
                cellData[targetRow.ToString()] = templateClone.DeepClone();

            FillLoopRow(cellData, targetRow, loop, items[i], warnings);
        }
    }

    private static List<JsonElement> GetArrayItems(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return [];
        if (!data.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray().ToList();
    }

    private static void InsertRowsBelow(JsonObject cellData, int afterRow, int count)
    {
        if (count <= 0)
            return;

        var keys = cellData
            .Select(p => int.TryParse(p.Key, out var r) ? r : (int?)null)
            .Where(r => r.HasValue && r.Value > afterRow)
            .Select(r => r!.Value)
            .OrderByDescending(r => r)
            .ToList();

        foreach (var row in keys)
        {
            var node = cellData[row.ToString()];
            if (node is null)
                continue;
            cellData.Remove(row.ToString());
            cellData[(row + count).ToString()] = node.DeepClone();
        }
    }

    private static void DeleteRow(JsonObject cellData, int rowIndex)
    {
        cellData.Remove(rowIndex.ToString());

        var keys = cellData
            .Select(p => int.TryParse(p.Key, out var r) ? r : (int?)null)
            .Where(r => r.HasValue && r.Value > rowIndex)
            .Select(r => r!.Value)
            .OrderBy(r => r)
            .ToList();

        foreach (var row in keys)
        {
            var node = cellData[row.ToString()];
            if (node is null)
                continue;
            cellData.Remove(row.ToString());
            cellData[(row - 1).ToString()] = node.DeepClone();
        }
    }

    private static void FillLoopRow(
        JsonObject cellData,
        int rowIndex,
        TemplateLoop loop,
        JsonElement item,
        List<string> warnings)
    {
        if (cellData[rowIndex.ToString()] is not JsonObject row)
            return;

        foreach (var colProp in row.ToList())
        {
            if (colProp.Value is not JsonObject cell)
                continue;
            if (cell["v"] is not JsonValue vNode)
                continue;

            var text = TryGetString(vNode);
            if (string.IsNullOrEmpty(text))
                continue;

            var newText = PlaceholderRegex.Replace(text, match =>
            {
                var prefix = match.Groups[1].Value;
                var name = match.Groups[2].Value;
                var prop = match.Groups[3].Success ? match.Groups[3].Value : null;

                if (prefix is "#" or "/")
                    return match.Value; // stripped later

                if (prop is null || !string.Equals(name, loop.Name, StringComparison.Ordinal))
                    return match.Value;

                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty(prop, out var val) &&
                    val.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    return JsonElementToString(val);
                }

                warnings.Add($"Missing field '{loop.Name}.{prop}' in loop item.");
                return match.Value;
            });

            cell["v"] = newText;
        }
    }

    private static void ReplaceSimpleFields(
        JsonObject sheets,
        JsonElement data,
        List<string> warnings)
    {
        var missingReported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sheetProp in sheets)
        {
            if (sheetProp.Value is not JsonObject sheet)
                continue;
            if (sheet["cellData"] is not JsonObject cellData)
                continue;

            foreach (var rowProp in cellData)
            {
                if (rowProp.Value is not JsonObject row)
                    continue;

                foreach (var colProp in row)
                {
                    if (colProp.Value is not JsonObject cell)
                        continue;
                    if (cell["v"] is not JsonValue vNode)
                        continue;

                    var text = TryGetString(vNode);
                    if (string.IsNullOrEmpty(text))
                        continue;

                    var newText = PlaceholderRegex.Replace(text, match =>
                    {
                        var prefix = match.Groups[1].Value;
                        var name = match.Groups[2].Value;
                        var prop = match.Groups[3].Success ? match.Groups[3].Value : null;

                        if (prefix is "#" or "/" || prop is not null)
                            return match.Value;

                        if (data.ValueKind == JsonValueKind.Object &&
                            data.TryGetProperty(name, out var val) &&
                            val.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                        {
                            return JsonElementToString(val);
                        }

                        if (missingReported.Add(name))
                            warnings.Add($"Missing field '{name}'.");
                        return match.Value;
                    });

                    cell["v"] = newText;
                }
            }
        }
    }

    private static void StripLoopMarkers(JsonObject sheets)
    {
        foreach (var sheetProp in sheets)
        {
            if (sheetProp.Value is not JsonObject sheet)
                continue;
            if (sheet["cellData"] is not JsonObject cellData)
                continue;

            foreach (var rowProp in cellData)
            {
                if (rowProp.Value is not JsonObject row)
                    continue;

                foreach (var colProp in row)
                {
                    if (colProp.Value is not JsonObject cell)
                        continue;
                    if (cell["v"] is not JsonValue vNode)
                        continue;

                    var text = TryGetString(vNode);
                    if (string.IsNullOrEmpty(text))
                        continue;

                    var stripped = PlaceholderRegex.Replace(text, match =>
                    {
                        var prefix = match.Groups[1].Value;
                        return prefix is "#" or "/" ? string.Empty : match.Value;
                    });

                    cell["v"] = stripped;
                }
            }
        }
    }

    private static string TryGetString(JsonValue vNode)
    {
        try
        {
            return vNode.GetValue<string>() ?? string.Empty;
        }
        catch
        {
            return vNode.ToJsonString().Trim('"');
        }
    }

    private static string JsonElementToString(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => el.GetRawText()
        };
}
