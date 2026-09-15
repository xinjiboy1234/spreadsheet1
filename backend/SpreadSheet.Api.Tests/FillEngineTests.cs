using System.Text.Json;
using System.Text.Json.Nodes;
using SpreadSheet.Api.Models;
using SpreadSheet.Api.Services;

namespace SpreadSheet.Api.Tests;

public class FillEngineTests
{
    private readonly FillEngine _engine = new();

    [Fact]
    public void Fill_SimpleReplace_MissingFieldKeepsPlaceholderAndWarns()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("客户:{{CustomerName}}"),
                    ["1"] = Cell("日期:{{OrderDate}}")
                }
            });

        var schema = new TemplateSchema
        {
            Fields = ["CustomerName", "OrderDate"]
        };
        using var data = JsonDocument.Parse("""{"CustomerName":"Acme"}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Contains("客户:Acme", GetCellV(result.WorkbookJson, "s1", 0, 0));
        Assert.Equal("日期:{{OrderDate}}", GetCellV(result.WorkbookJson, "s1", 0, 1));
        Assert.Contains(result.Warnings, w => w.Contains("OrderDate", StringComparison.Ordinal));
    }

    [Fact]
    public void Fill_LoopItemMissingField_KeepsPlaceholderAndWarns()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}"),
                    ["2"] = Cell("{{Items.Qty}}"),
                    ["3"] = Cell("{{/Items}}")
                }
            });

        var schema = new TemplateSchema
        {
            Loops =
            [
                new TemplateLoop
                {
                    Name = "Items",
                    Sheet = "Sheet1",
                    StartRow = 1,
                    EndRow = 1,
                    Fields = ["Name", "Qty"]
                }
            ]
        };
        using var data = JsonDocument.Parse("""{"Items":[{"Name":"Widget"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("Widget", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("{{Items.Qty}}", GetCellV(result.WorkbookJson, "s1", 1, 2));
        Assert.DoesNotContain("{{#Items}}", GetCellV(result.WorkbookJson, "s1", 1, 0) ?? "");
        Assert.DoesNotContain("{{/Items}}", GetCellV(result.WorkbookJson, "s1", 1, 3) ?? "");
        Assert.Contains(result.Warnings, w => w.Contains("Qty", StringComparison.Ordinal));
    }

    [Fact]
    public void Fill_LoopN2_InsertsOneRowBelowBothFilled()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}"),
                    ["2"] = Cell("{{/Items}}")
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("footer")
                }
            });

        var schema = new TemplateSchema
        {
            Loops =
            [
                new TemplateLoop
                {
                    Name = "Items",
                    Sheet = "Sheet1",
                    StartRow = 1,
                    EndRow = 1,
                    Fields = ["Name"]
                }
            ]
        };
        using var data = JsonDocument.Parse("""{"Items":[{"Name":"A"},{"Name":"B"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("A", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("B", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Equal("footer", GetCellV(result.WorkbookJson, "s1", 3, 0));
        Assert.DoesNotContain("{{#", result.WorkbookJson);
        Assert.DoesNotContain("{{/", result.WorkbookJson);
    }

    [Fact]
    public void Fill_LoopN0OrMissingArray_DeletesTemplateRow()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("header")
                },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}"),
                    ["2"] = Cell("{{/Items}}")
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("footer")
                }
            });

        var schema = new TemplateSchema
        {
            Loops =
            [
                new TemplateLoop
                {
                    Name = "Items",
                    Sheet = "Sheet1",
                    StartRow = 1,
                    EndRow = 1,
                    Fields = ["Name"]
                }
            ]
        };
        using var data = JsonDocument.Parse("""{"Items":[]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("header", GetCellV(result.WorkbookJson, "s1", 0, 0));
        Assert.Equal("footer", GetCellV(result.WorkbookJson, "s1", 1, 0));
        Assert.Null(GetCellV(result.WorkbookJson, "s1", 2, 0));

        using var missing = JsonDocument.Parse("{}");
        var resultMissing = _engine.Fill(workbook, missing.RootElement, schema);
        Assert.Equal("footer", GetCellV(resultMissing.WorkbookJson, "s1", 1, 0));
    }

    [Fact]
    public void Fill_SchemaMergeWarning_SkipsLoopAndWarns()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}"),
                    ["2"] = Cell("{{/Items}}")
                }
            });

        var schema = new TemplateSchema
        {
            Loops =
            [
                new TemplateLoop
                {
                    Name = "Items",
                    Sheet = "Sheet1",
                    StartRow = 1,
                    EndRow = 1,
                    Fields = ["Name"]
                }
            ],
            Warnings =
            [
                "Loop 'Items' on sheet 'Sheet1' overlaps a merge range."
            ]
        };
        using var data = JsonDocument.Parse("""{"Items":[{"Name":"A"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("{{Items.Name}}", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Contains(result.Warnings, w =>
            w.Contains("Items", StringComparison.Ordinal) &&
            (w.Contains("skip", StringComparison.OrdinalIgnoreCase) ||
             w.Contains("merge", StringComparison.OrdinalIgnoreCase) ||
             w.Contains("跳过", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fill_NestedLoopInSchema_SkipsAndWarns()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Outer}}"),
                    ["1"] = Cell("{{Outer.Name}}"),
                    ["2"] = Cell("{{/Outer}}")
                }
            });

        var schema = new TemplateSchema
        {
            Loops =
            [
                new TemplateLoop
                {
                    Name = "Outer",
                    Sheet = "Sheet1",
                    StartRow = 0,
                    EndRow = 0,
                    Fields = ["Name"]
                }
            ],
            Warnings =
            [
                "Nested loop 'Inner' inside 'Outer' on sheet 'Sheet1' is not supported."
            ]
        };
        using var data = JsonDocument.Parse("""{"Outer":[{"Name":"X"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("{{Outer.Name}}", GetCellV(result.WorkbookJson, "s1", 0, 1));
        Assert.Contains(result.Warnings, w =>
            w.Contains("Outer", StringComparison.Ordinal) &&
            (w.Contains("skip", StringComparison.OrdinalIgnoreCase) ||
             w.Contains("nest", StringComparison.OrdinalIgnoreCase) ||
             w.Contains("跳过", StringComparison.Ordinal)));
    }

    [Fact]
    public void Fill_TwoLoops_ProcessByStartRowDescending()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#First}}"),
                    ["1"] = Cell("{{First.Name}}"),
                    ["2"] = Cell("{{/First}}")
                },
                ["3"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Second}}"),
                    ["1"] = Cell("{{Second.Name}}"),
                    ["2"] = Cell("{{/Second}}")
                },
                ["4"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("end")
                }
            });

        var schema = new TemplateSchema
        {
            Loops =
            [
                new TemplateLoop
                {
                    Name = "First",
                    Sheet = "Sheet1",
                    StartRow = 1,
                    EndRow = 1,
                    Fields = ["Name"]
                },
                new TemplateLoop
                {
                    Name = "Second",
                    Sheet = "Sheet1",
                    StartRow = 3,
                    EndRow = 3,
                    Fields = ["Name"]
                }
            ]
        };
        using var data = JsonDocument.Parse(
            """{"First":[{"Name":"F1"},{"Name":"F2"}],"Second":[{"Name":"S1"},{"Name":"S2"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        // Second expands first (startRow desc): rows 3,4 = S1,S2; end shifts to 5
        // Then First expands: rows 1,2 = F1,F2; Second block shifts +1 → 4,5; end → 6
        Assert.Equal("F1", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("F2", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Equal("S1", GetCellV(result.WorkbookJson, "s1", 4, 1));
        Assert.Equal("S2", GetCellV(result.WorkbookJson, "s1", 5, 1));
        Assert.Equal("end", GetCellV(result.WorkbookJson, "s1", 6, 0));
    }

    [Fact]
    public void Fill_MultiRowLoop_SkipsWithWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}")
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{Items.Name}}")
                },
                ["3"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{/Items}}")
                }
            });

        var schema = new TemplateSchema
        {
            Loops =
            [
                new TemplateLoop
                {
                    Name = "Items",
                    Sheet = "Sheet1",
                    StartRow = 1,
                    EndRow = 3,
                    Fields = ["Name"]
                }
            ]
        };
        using var data = JsonDocument.Parse("""{"Items":[{"Name":"A"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("{{Items.Name}}", GetCellV(result.WorkbookJson, "s1", 2, 0));
        Assert.Contains(result.Warnings, w =>
            w.Contains("Items", StringComparison.Ordinal) &&
            (w.Contains("skip", StringComparison.OrdinalIgnoreCase) ||
             w.Contains("multi", StringComparison.OrdinalIgnoreCase) ||
             w.Contains("跳过", StringComparison.Ordinal)));
    }

    private static string Workbook(
        string sheet,
        string name,
        Dictionary<string, object> cellData)
    {
        var workbook = new Dictionary<string, object>
        {
            ["id"] = "wb1",
            ["sheetOrder"] = new[] { sheet },
            ["sheets"] = new Dictionary<string, object>
            {
                [sheet] = new Dictionary<string, object>
                {
                    ["id"] = sheet,
                    ["name"] = name,
                    ["cellData"] = cellData
                }
            }
        };
        return JsonSerializer.Serialize(workbook);
    }

    private static Dictionary<string, object> Cell(string v) =>
        new() { ["v"] = v };

    private static string? GetCellV(string workbookJson, string sheetId, int row, int col)
    {
        var root = JsonNode.Parse(workbookJson)!.AsObject();
        var sheets = root["sheets"]!.AsObject();
        var sheet = sheets[sheetId]!.AsObject();
        if (sheet["cellData"] is not JsonObject cellData)
            return null;
        if (cellData[row.ToString()] is not JsonObject rowObj)
            return null;
        if (rowObj[col.ToString()] is not JsonObject cell)
            return null;
        return cell["v"]?.GetValue<string>();
    }
}
