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

/// <summary>
/// Renders a template workbook by rebuilding each sheet row by row. Loop blocks may span
/// multiple rows and nest; merge ranges, row heights and freeze anchors follow the
/// template-row to output-row mapping produced while rendering.
/// </summary>
public class FillEngine
{
    private const int MaxOutputRows = 100_000;

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

        var matchedSheets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sheetProp in sheets)
        {
            if (sheetProp.Value is not JsonObject sheetObj)
                continue;

            var sheetName = AsString(sheetObj["name"]) ?? sheetProp.Key;
            var loops = schema.Loops
                .Where(l => string.Equals(l.Sheet, sheetName, StringComparison.Ordinal))
                .ToList();

            if (loops.Count > 0)
                matchedSheets.Add(sheetName);

            var blocks = BuildBlockTree(loops, sheetName, warnings);
            new SheetRenderer(sheetObj, blocks, data, warnings).Render();
        }

        WarnMissingSheets(schema, matchedSheets, warnings);

        return new FillResult
        {
            WorkbookJson = root.ToJsonString(),
            Warnings = warnings
        };
    }

    private static void WarnMissingSheets(
        TemplateSchema schema,
        HashSet<string> matchedSheets,
        List<string> warnings)
    {
        foreach (var missing in schema.Loops
            .Select(l => l.Sheet)
            .Distinct(StringComparer.Ordinal)
            .Where(s => !matchedSheets.Contains(s))
            .OrderBy(s => s, StringComparer.Ordinal))
        {
            var names = string.Join(", ", schema.Loops
                .Where(l => string.Equals(l.Sheet, missing, StringComparison.Ordinal))
                .Select(l => $"'{l.Name}'"));
            warnings.Add($"Sheet '{missing}' not found; loops {names} skipped.");
        }
    }

    /// <summary>
    /// Rebuilds the parent/child structure from the flat loop list using row containment, so
    /// schemas persisted before ParentName/Depth existed still nest correctly.
    /// </summary>
    private static List<Block> BuildBlockTree(
        List<TemplateLoop> loops,
        string sheetName,
        List<string> warnings)
    {
        var roots = new List<Block>();
        if (loops.Count == 0)
            return roots;

        var ordered = loops
            .OrderBy(l => l.StartRow)
            .ThenByDescending(l => l.EndRow)
            .ThenBy(l => l.Depth)
            .ThenBy(l => l.Name, StringComparer.Ordinal)
            .ToList();

        var stack = new List<Block>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var loop in ordered)
        {
            if (loop.StartRow < 0 || loop.EndRow < loop.StartRow)
            {
                warnings.Add(
                    $"Loop '{loop.Name}' on sheet '{sheetName}' has an invalid row range; skipped.");
                continue;
            }

            if (!seen.Add(loop.Name))
            {
                warnings.Add(
                    $"Duplicate loop name '{loop.Name}' on sheet '{sheetName}'; the later block is skipped.");
                continue;
            }

            while (stack.Count > 0 && stack[^1].EndRow < loop.StartRow)
                stack.RemoveAt(stack.Count - 1);

            if (stack.Count > 0 && loop.EndRow > stack[^1].EndRow)
            {
                warnings.Add(
                    $"Loop '{loop.Name}' on sheet '{sheetName}' crosses loop '{stack[^1].Name}'; skipped.");
                continue;
            }

            var block = new Block(loop.Name, loop.StartRow, loop.EndRow);
            if (stack.Count > 0)
                stack[^1].Children.Add(block);
            else
                roots.Add(block);
            stack.Add(block);
        }

        return roots;
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? AsInt(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<int>(out var i))
            return i;
        if (v.TryGetValue<double>(out var d))
            return (int)d;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed))
            return parsed;
        return null;
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

    private sealed class Block(string name, int startRow, int endRow)
    {
        public string Name { get; } = name;
        public int StartRow { get; } = startRow;
        public int EndRow { get; } = endRow;
        public List<Block> Children { get; } = [];
    }

    private sealed class Scope(string name, JsonElement item, Scope? parent)
    {
        public string Name { get; } = name;
        public JsonElement Item { get; } = item;
        public Scope? Parent { get; } = parent;
    }

    private sealed record MergeRect(int StartRow, int EndRow, JsonObject Source);

    private sealed class SheetRenderer
    {
        private readonly JsonObject _sheet;
        private readonly List<Block> _roots;
        private readonly List<Block> _allBlocks = [];
        private readonly JsonElement _data;
        private readonly List<string> _warnings;

        private readonly JsonObject? _srcCellData;
        private readonly JsonObject? _srcRowData;
        private readonly List<MergeRect> _srcMerges = [];
        private readonly bool _hasMergeData;

        private readonly JsonObject _outCellData = new();
        private readonly JsonObject _outRowData = new();
        private readonly JsonArray _outMerges = new();
        private readonly Dictionary<int, List<int>> _rowMap = [];
        private readonly Dictionary<string, List<(int Start, int End)>> _instances = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

        private int _outRow;
        private bool _truncated;

        public SheetRenderer(JsonObject sheet, List<Block> roots, JsonElement data, List<string> warnings)
        {
            _sheet = sheet;
            _roots = roots;
            _data = data;
            _warnings = warnings;

            _srcCellData = sheet["cellData"] as JsonObject;
            _srcRowData = sheet["rowData"] as JsonObject;

            if (sheet["mergeData"] is JsonArray merges)
            {
                _hasMergeData = true;
                foreach (var node in merges)
                {
                    if (node is not JsonObject obj)
                        continue;
                    var sr = AsInt(obj["startRow"]);
                    var er = AsInt(obj["endRow"]);
                    if (sr is null || er is null)
                        continue;
                    _srcMerges.Add(new MergeRect(sr.Value, er.Value, obj));
                }
            }

            Flatten(roots);
        }

        private void Flatten(List<Block> blocks)
        {
            foreach (var b in blocks)
            {
                _allBlocks.Add(b);
                Flatten(b.Children);
            }
        }

        public void Render()
        {
            var maxRow = ComputeMaxRow();
            if (maxRow >= 0)
                RenderRange(0, maxRow, _roots, null);

            RewriteMerges();
            WriteBack();
        }

        private int ComputeMaxRow()
        {
            var max = -1;

            if (_srcCellData is not null)
            {
                foreach (var p in _srcCellData)
                {
                    if (int.TryParse(p.Key, out var r) && r > max)
                        max = r;
                }
            }

            if (_srcRowData is not null)
            {
                foreach (var p in _srcRowData)
                {
                    if (int.TryParse(p.Key, out var r) && r > max)
                        max = r;
                }
            }

            foreach (var m in _srcMerges)
            {
                if (m.EndRow > max)
                    max = m.EndRow;
            }

            foreach (var b in _allBlocks)
            {
                if (b.EndRow > max)
                    max = b.EndRow;
            }

            return max;
        }

        private void RenderRange(int templateStart, int templateEnd, List<Block> levelBlocks, Scope? scope)
        {
            var t = templateStart;
            while (t <= templateEnd && !_truncated)
            {
                var block = levelBlocks.FirstOrDefault(b => b.StartRow == t);
                if (block is null)
                {
                    EmitRow(t, scope);
                    t++;
                    continue;
                }

                foreach (var item in ResolveItems(block, scope))
                {
                    var instanceStart = _outRow;
                    RenderRange(block.StartRow, block.EndRow, block.Children, new Scope(block.Name, item, scope));
                    if (_outRow > instanceStart)
                        AddInstance(block.Name, instanceStart, _outRow - 1);
                    if (_truncated)
                        break;
                }

                t = block.EndRow + 1;
            }
        }

        private List<JsonElement> ResolveItems(Block block, Scope? scope)
        {
            for (var cur = scope; cur is not null; cur = cur.Parent)
            {
                if (cur.Item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!cur.Item.TryGetProperty(block.Name, out var nested))
                    continue;
                return nested.ValueKind == JsonValueKind.Array
                    ? nested.EnumerateArray().ToList()
                    : [];
            }

            if (_data.ValueKind == JsonValueKind.Object &&
                _data.TryGetProperty(block.Name, out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                return arr.EnumerateArray().ToList();
            }

            return [];
        }

        private void AddInstance(string name, int start, int end)
        {
            if (!_instances.TryGetValue(name, out var list))
            {
                list = [];
                _instances[name] = list;
            }
            list.Add((start, end));
        }

        private void EmitRow(int templateRow, Scope? scope)
        {
            if (_outRow >= MaxOutputRows)
            {
                if (!_truncated)
                {
                    _truncated = true;
                    _warnings.Add(
                        $"Rendering stopped at {MaxOutputRows} rows; the fill data expands the template beyond the supported size.");
                }
                return;
            }

            var outRow = _outRow++;

            if (!_rowMap.TryGetValue(templateRow, out var mapped))
            {
                mapped = [];
                _rowMap[templateRow] = mapped;
            }
            mapped.Add(outRow);

            if (_srcRowData?[templateRow.ToString()] is JsonNode rowMeta)
                _outRowData[outRow.ToString()] = rowMeta.DeepClone();

            if (_srcCellData?[templateRow.ToString()] is not JsonObject srcRow)
                return;

            var newRow = new JsonObject();
            foreach (var colProp in srcRow)
            {
                if (colProp.Value is null)
                    continue;

                var clone = colProp.Value.DeepClone();
                if (clone is JsonObject cell && AsString(cell["v"]) is { } text &&
                    text.Contains("{{", StringComparison.Ordinal))
                {
                    cell["v"] = ReplacePlaceholders(text, scope);
                }

                newRow[colProp.Key] = clone;
            }

            _outCellData[outRow.ToString()] = newRow;
        }

        private string ReplacePlaceholders(string text, Scope? scope) =>
            PlaceholderRegex.Replace(text, match =>
            {
                var prefix = match.Groups[1].Value;
                var name = match.Groups[2].Value;
                var prop = match.Groups[3].Success ? match.Groups[3].Value : null;

                if (prefix is "#" or "/")
                    return string.Empty;

                if (prop is null)
                    return ResolveRootField(name, match.Value);

                for (var cur = scope; cur is not null; cur = cur.Parent)
                {
                    if (!string.Equals(cur.Name, name, StringComparison.Ordinal))
                        continue;

                    if (cur.Item.ValueKind == JsonValueKind.Object &&
                        cur.Item.TryGetProperty(prop, out var val) &&
                        val.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                    {
                        return JsonElementToString(val);
                    }

                    Report($"missing-loop:{name}.{prop}", $"Missing field '{name}.{prop}' in loop item.");
                    return match.Value;
                }

                Report(
                    $"out-of-scope:{name}.{prop}",
                    $"Placeholder '{{{{{name}.{prop}}}}}' is outside loop '{name}'; left as-is.");
                return match.Value;
            });

        private string ResolveRootField(string name, string original)
        {
            if (_data.ValueKind == JsonValueKind.Object &&
                _data.TryGetProperty(name, out var val) &&
                val.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return JsonElementToString(val);
            }

            Report($"missing:{name}", $"Missing field '{name}'.");
            return original;
        }

        private void Report(string key, string message)
        {
            if (_reported.Add(key))
                _warnings.Add(message);
        }

        private void RewriteMerges()
        {
            foreach (var m in _srcMerges)
            {
                var block = FindInnermostContaining(m.StartRow, m.EndRow);

                if (block is null)
                {
                    if (_allBlocks.Any(b => m.StartRow <= b.EndRow && m.EndRow >= b.StartRow))
                    {
                        _warnings.Add(
                            $"Merge range rows {m.StartRow}-{m.EndRow} crosses a loop boundary; dropped.");
                        continue;
                    }

                    var outside = OutputRowsFor(m.StartRow, m.EndRow, int.MinValue, int.MaxValue);
                    if (outside.Count == 0)
                        continue;

                    AddMerge(m, outside.Min(), outside.Max());
                    continue;
                }

                if (!_instances.TryGetValue(block.Name, out var instances))
                    continue;

                foreach (var (instanceStart, instanceEnd) in instances)
                {
                    // Rows that actually materialised for this instance; a deleted inner block
                    // simply shrinks the merge instead of invalidating it.
                    var rows = OutputRowsFor(m.StartRow, m.EndRow, instanceStart, instanceEnd);
                    if (rows.Count == 0)
                        continue;

                    AddMerge(m, rows.Min(), rows.Max());
                }
            }
        }

        /// <summary>Innermost block whose row range fully contains the template rows, or null.</summary>
        private Block? FindInnermostContaining(int startRow, int endRow)
        {
            Block? best = null;
            foreach (var b in _allBlocks)
            {
                if (b.StartRow > startRow || b.EndRow < endRow)
                    continue;
                if (best is null || b.EndRow - b.StartRow < best.EndRow - best.StartRow)
                    best = b;
            }
            return best;
        }

        private IEnumerable<int> RowsFor(int templateRow) =>
            _rowMap.TryGetValue(templateRow, out var rows) ? rows : [];

        private List<int> OutputRowsFor(int templateStart, int templateEnd, int boundStart, int boundEnd)
        {
            var rows = new List<int>();
            for (var t = templateStart; t <= templateEnd; t++)
            {
                foreach (var r in RowsFor(t))
                {
                    if (r >= boundStart && r <= boundEnd)
                        rows.Add(r);
                }
            }
            return rows;
        }

        private void AddMerge(MergeRect source, int startRow, int endRow)
        {
            var clone = source.Source.DeepClone().AsObject();
            clone["startRow"] = startRow;
            clone["endRow"] = endRow;
            _outMerges.Add(clone);
        }

        private void WriteBack()
        {
            if (_srcCellData is not null)
                _sheet["cellData"] = _outCellData;
            if (_srcRowData is not null)
                _sheet["rowData"] = _outRowData;
            if (_hasMergeData)
                _sheet["mergeData"] = _outMerges;

            var rowCount = AsInt(_sheet["rowCount"]) ?? 0;
            if (_outRow > rowCount)
                _sheet["rowCount"] = _outRow;

            RemapFreeze();
        }

        private void RemapFreeze()
        {
            if (_sheet["freeze"] is not JsonObject freeze)
                return;

            var startRow = AsInt(freeze["startRow"]);
            if (startRow is null || startRow <= 0)
                return;

            var mapped = RowsFor(startRow.Value).ToList();
            if (mapped.Count > 0)
                freeze["startRow"] = mapped.Min();
        }
    }
}
