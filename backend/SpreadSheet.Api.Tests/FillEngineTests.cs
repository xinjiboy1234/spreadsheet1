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

        var schema = Schema(Loop("Items", 1, 1, "Name", "Qty"));
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

        var schema = Schema(Loop("Items", 1, 1, "Name"));
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

        var schema = Schema(Loop("Items", 1, 1, "Name"));
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
    public void Fill_TwoSiblingLoops_BothExpandAndShiftFollowingRows()
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

        var schema = Schema(
            Loop("First", 1, 1, "Name"),
            Loop("Second", 3, 3, "Name"));
        using var data = JsonDocument.Parse(
            """{"First":[{"Name":"F1"},{"Name":"F2"}],"Second":[{"Name":"S1"},{"Name":"S2"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("F1", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("F2", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Equal("S1", GetCellV(result.WorkbookJson, "s1", 4, 1));
        Assert.Equal("S2", GetCellV(result.WorkbookJson, "s1", 5, 1));
        Assert.Equal("end", GetCellV(result.WorkbookJson, "s1", 6, 0));
    }

    [Fact]
    public void Fill_MultiRowLoop_ExpandsEveryRowOfTheBlock()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}")
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["1"] = Cell("{{Items.Note}}")
                },
                ["3"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{/Items}}"),
                    ["1"] = Cell("detail")
                },
                ["4"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("footer")
                }
            });

        var schema = Schema(Loop("Items", 1, 3, "Name", "Note"));
        using var data = JsonDocument.Parse(
            """{"Items":[{"Name":"A","Note":"na"},{"Name":"B","Note":"nb"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("A", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("na", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Equal("detail", GetCellV(result.WorkbookJson, "s1", 3, 1));
        Assert.Equal("B", GetCellV(result.WorkbookJson, "s1", 4, 1));
        Assert.Equal("nb", GetCellV(result.WorkbookJson, "s1", 5, 1));
        Assert.Equal("detail", GetCellV(result.WorkbookJson, "s1", 6, 1));
        Assert.Equal("footer", GetCellV(result.WorkbookJson, "s1", 7, 0));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Fill_MergeInsideBlock_ClonedForEachInstance()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["0"] = Cell("header") },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#P}}"),
                    ["1"] = Cell("{{P.Desc}}")
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{/P}}"),
                    ["1"] = Cell("{{P.Part}}")
                }
            },
            mergeData: [Merge(1, 1, 1, 3)]);

        var schema = Schema(Loop("P", 1, 2, "Desc", "Part"));
        using var data = JsonDocument.Parse(
            """{"P":[{"Desc":"d1","Part":"p1"},{"Desc":"d2","Part":"p2"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("d1", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("p1", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Equal("d2", GetCellV(result.WorkbookJson, "s1", 3, 1));
        Assert.Equal("p2", GetCellV(result.WorkbookJson, "s1", 4, 1));

        var merges = GetMerges(result.WorkbookJson, "s1");
        Assert.Equal(2, merges.Count);
        Assert.Contains((1, 1, 1, 3), merges);
        Assert.Contains((3, 3, 1, 3), merges);
    }

    [Fact]
    public void Fill_MergeSpanningNestedBlock_StretchesToInstanceHeight()
    {
        var workbook = ProductSpecWorkbook();
        var schema = ProductSpecSchema();
        using var data = JsonDocument.Parse(
            """
            {"P":[{"Desc":"d1","Part":"p1","Qty":1,
                   "O":[{"Code":"323"},{"Code":"N"},{"Code":"R"}]}]}
            """);

        var result = _engine.Fill(workbook, data.RootElement, schema);

        // Outer instance occupies rows 1..5: description, part number, then three option rows.
        Assert.Equal("d1", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("p1", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Equal("323", GetCellV(result.WorkbookJson, "s1", 3, 0));
        Assert.Equal("N", GetCellV(result.WorkbookJson, "s1", 4, 0));
        Assert.Equal("R", GetCellV(result.WorkbookJson, "s1", 5, 0));

        var merges = GetMerges(result.WorkbookJson, "s1");
        Assert.Contains((1, 5, 4, 4), merges);   // Qty merged across the whole block
        Assert.Contains((1, 1, 1, 3), merges);   // description row merge, unchanged height
        Assert.Contains((3, 3, 1, 2), merges);   // per-option merge, one per option row
        Assert.Contains((4, 4, 1, 2), merges);
        Assert.Contains((5, 5, 1, 2), merges);
    }

    [Fact]
    public void Fill_NestedLoop_ResolvesInnerArrayFromOuterItem()
    {
        var workbook = ProductSpecWorkbook();
        var schema = ProductSpecSchema();
        using var data = JsonDocument.Parse(
            """
            {"P":[{"Desc":"d1","Part":"p1","Qty":1,
                   "O":[{"Code":"a1","Desc":"x"},{"Code":"a2","Desc":"x"}]},
                  {"Desc":"d2","Part":"p2","Qty":2,
                   "O":[{"Code":"b1","Desc":"x"},{"Code":"b2","Desc":"x"},{"Code":"b3","Desc":"x"}]}]}
            """);

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("a1", GetCellV(result.WorkbookJson, "s1", 3, 0));
        Assert.Equal("a2", GetCellV(result.WorkbookJson, "s1", 4, 0));
        Assert.Equal("d2", GetCellV(result.WorkbookJson, "s1", 5, 1));
        Assert.Equal("p2", GetCellV(result.WorkbookJson, "s1", 6, 1));
        Assert.Equal("b1", GetCellV(result.WorkbookJson, "s1", 7, 0));
        Assert.Equal("b3", GetCellV(result.WorkbookJson, "s1", 9, 0));

        var merges = GetMerges(result.WorkbookJson, "s1");
        Assert.Contains((1, 4, 4, 4), merges);   // first product: 2 rows + 2 options
        Assert.Contains((5, 9, 4, 4), merges);   // second product: 2 rows + 3 options
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Fill_InnerArrayEmpty_DropsInnerRowsAndShrinksSpanningMerge()
    {
        var workbook = ProductSpecWorkbook();
        var schema = ProductSpecSchema();
        using var data = JsonDocument.Parse(
            """{"P":[{"Desc":"d1","Part":"p1","Qty":1,"O":[]}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("d1", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("p1", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Null(GetCellV(result.WorkbookJson, "s1", 3, 0));

        var merges = GetMerges(result.WorkbookJson, "s1");
        Assert.Contains((1, 2, 4, 4), merges);
        Assert.DoesNotContain(merges, m => m.StartColumn == 1 && m.EndColumn == 2);
    }

    [Fact]
    public void Fill_OuterArrayEmpty_DropsWholeBlockAndItsMerges()
    {
        var workbook = ProductSpecWorkbook();
        var schema = ProductSpecSchema();
        using var data = JsonDocument.Parse("""{"P":[]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("head", GetCellV(result.WorkbookJson, "s1", 0, 0));
        Assert.Null(GetCellV(result.WorkbookJson, "s1", 1, 1));

        var merges = GetMerges(result.WorkbookJson, "s1");
        Assert.Single(merges);
        Assert.Equal((0, 0, 0, 3), merges[0]);
    }

    [Fact]
    public void Fill_MergeOutsideBlocks_ShiftsWithRowMapping()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["0"] = Cell("title") },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}"),
                    ["2"] = Cell("{{/Items}}")
                },
                ["2"] = new Dictionary<string, object> { ["0"] = Cell("total") }
            },
            mergeData: [Merge(0, 0, 0, 2), Merge(2, 2, 0, 2)]);

        var schema = Schema(Loop("Items", 1, 1, "Name"));
        using var data = JsonDocument.Parse(
            """{"Items":[{"Name":"A"},{"Name":"B"},{"Name":"C"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("total", GetCellV(result.WorkbookJson, "s1", 4, 0));

        var merges = GetMerges(result.WorkbookJson, "s1");
        Assert.Contains((0, 0, 0, 2), merges);
        Assert.Contains((4, 4, 0, 2), merges);
    }

    [Fact]
    public void Fill_MergeCrossingBlockBoundary_DroppedWithWarning()
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
                ["2"] = new Dictionary<string, object> { ["0"] = Cell("footer") }
            },
            mergeData: [Merge(1, 2, 0, 0)]);

        var schema = Schema(Loop("Items", 1, 1, "Name"));
        using var data = JsonDocument.Parse("""{"Items":[{"Name":"A"},{"Name":"B"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("A", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Equal("B", GetCellV(result.WorkbookJson, "s1", 2, 1));
        Assert.Empty(GetMerges(result.WorkbookJson, "s1"));
        Assert.Contains(result.Warnings, w => w.Contains("crosses a loop boundary", StringComparison.Ordinal));
    }

    [Fact]
    public void Fill_RowHeights_FollowBlockExpansion()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["0"] = Cell("header") },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}"),
                    ["2"] = Cell("{{/Items}}")
                },
                ["2"] = new Dictionary<string, object> { ["0"] = Cell("footer") }
            },
            rowData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["h"] = 19 },
                ["1"] = new Dictionary<string, object> { ["h"] = 40 },
                ["2"] = new Dictionary<string, object> { ["h"] = 25 }
            });

        var schema = Schema(Loop("Items", 1, 1, "Name"));
        using var data = JsonDocument.Parse("""{"Items":[{"Name":"A"},{"Name":"B"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal(19, GetRowHeight(result.WorkbookJson, "s1", 0));
        Assert.Equal(40, GetRowHeight(result.WorkbookJson, "s1", 1));
        Assert.Equal(40, GetRowHeight(result.WorkbookJson, "s1", 2));
        Assert.Equal(25, GetRowHeight(result.WorkbookJson, "s1", 3));
    }

    [Fact]
    public void Fill_SparseCellData_KeepsEmptyRowsInPlace()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["0"] = Cell("{{Title}}") },
                ["5"] = new Dictionary<string, object> { ["0"] = Cell("tail") }
            });

        using var data = JsonDocument.Parse("""{"Title":"T"}""");

        var result = _engine.Fill(workbook, data.RootElement, new TemplateSchema());

        Assert.Equal("T", GetCellV(result.WorkbookJson, "s1", 0, 0));
        Assert.Equal("tail", GetCellV(result.WorkbookJson, "s1", 5, 0));
        Assert.Null(GetCellV(result.WorkbookJson, "s1", 1, 0));
    }

    [Fact]
    public void Fill_FreezeAnchorBelowLoop_IsRemapped()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["0"] = Cell("header") },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#Items}}"),
                    ["1"] = Cell("{{Items.Name}}"),
                    ["2"] = Cell("{{/Items}}")
                },
                ["2"] = new Dictionary<string, object> { ["0"] = Cell("spacer") },
                ["3"] = new Dictionary<string, object> { ["0"] = Cell("body") }
            },
            freeze: new Dictionary<string, object>
            {
                ["xSplit"] = 0,
                ["ySplit"] = 3,
                ["startRow"] = 3,
                ["startColumn"] = -1
            });

        var schema = Schema(Loop("Items", 1, 1, "Name"));
        using var data = JsonDocument.Parse(
            """{"Items":[{"Name":"A"},{"Name":"B"},{"Name":"C"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("body", GetCellV(result.WorkbookJson, "s1", 5, 0));
        Assert.Equal(5, GetFreezeStartRow(result.WorkbookJson, "s1"));
    }

    [Fact]
    public void Fill_LoopOnUnknownSheet_WarnsAndLeavesTemplateAlone()
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
                    Sheet = "Missing",
                    StartRow = 1,
                    EndRow = 1,
                    Fields = ["Name"]
                }
            ]
        };
        using var data = JsonDocument.Parse("""{"Items":[{"Name":"A"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("{{Items.Name}}", GetCellV(result.WorkbookJson, "s1", 1, 1));
        Assert.Contains(result.Warnings, w => w.Contains("Missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Fill_CrossingLoops_SkipsInnerBlockWithWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["0"] = Cell("{{A.Name}}") },
                ["1"] = new Dictionary<string, object> { ["0"] = Cell("{{B.Name}}") }
            });

        var schema = Schema(
            Loop("A", 0, 1, "Name"),
            Loop("B", 1, 2, "Name"));
        using var data = JsonDocument.Parse("""{"A":[{"Name":"a"}],"B":[{"Name":"b"}]}""");

        var result = _engine.Fill(workbook, data.RootElement, schema);

        Assert.Equal("a", GetCellV(result.WorkbookJson, "s1", 0, 0));
        Assert.Contains(result.Warnings, w => w.Contains("crosses loop", StringComparison.Ordinal));
    }

    /// <summary>
    /// Mirrors the product-specification layout: a header row, then a variable-height product
    /// block whose Tags/Qty cells are merged down to the last row of the block, with an inner
    /// loop over option codes.
    /// </summary>
    private static string ProductSpecWorkbook() =>
        Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object> { ["0"] = Cell("head") },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#P}}"),
                    ["1"] = Cell("{{P.Desc}}"),
                    ["4"] = Cell("{{P.Qty}}")
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["1"] = Cell("{{P.Part}}")
                },
                ["3"] = new Dictionary<string, object>
                {
                    ["0"] = Cell("{{#O}}{{O.Code}}"),
                    ["1"] = Cell("{{O.Desc}}"),
                    ["2"] = Cell("{{/O}}{{/P}}")
                }
            },
            mergeData:
            [
                Merge(0, 0, 0, 3),
                Merge(1, 1, 1, 3),
                Merge(2, 2, 1, 3),
                Merge(1, 3, 4, 4),
                Merge(3, 3, 1, 2)
            ]);

    private static TemplateSchema ProductSpecSchema() =>
        Schema(
            Loop("P", 1, 3, "Desc", "Part", "Qty"),
            NestedLoop("O", 3, 3, "P", 1, "Code", "Desc"));

    private static TemplateSchema Schema(params TemplateLoop[] loops) =>
        new() { Loops = [.. loops] };

    private static TemplateLoop Loop(string name, int startRow, int endRow, params string[] fields) =>
        new()
        {
            Name = name,
            Sheet = "Sheet1",
            StartRow = startRow,
            EndRow = endRow,
            Fields = [.. fields]
        };

    private static TemplateLoop NestedLoop(
        string name,
        int startRow,
        int endRow,
        string parentName,
        int depth,
        params string[] fields)
    {
        var loop = Loop(name, startRow, endRow, fields);
        loop.ParentName = parentName;
        loop.Depth = depth;
        return loop;
    }

    private static string Workbook(
        string sheet,
        string name,
        Dictionary<string, object> cellData,
        List<Dictionary<string, object>>? mergeData = null,
        Dictionary<string, object>? rowData = null,
        Dictionary<string, object>? freeze = null)
    {
        var sheetObj = new Dictionary<string, object>
        {
            ["id"] = sheet,
            ["name"] = name,
            ["cellData"] = cellData
        };
        if (mergeData is not null)
            sheetObj["mergeData"] = mergeData;
        if (rowData is not null)
            sheetObj["rowData"] = rowData;
        if (freeze is not null)
            sheetObj["freeze"] = freeze;

        var workbook = new Dictionary<string, object>
        {
            ["id"] = "wb1",
            ["sheetOrder"] = new[] { sheet },
            ["sheets"] = new Dictionary<string, object>
            {
                [sheet] = sheetObj
            }
        };
        return JsonSerializer.Serialize(workbook);
    }

    private static Dictionary<string, object> Cell(string v) =>
        new() { ["v"] = v };

    private static Dictionary<string, object> Merge(int startRow, int endRow, int startColumn, int endColumn) =>
        new()
        {
            ["startRow"] = startRow,
            ["endRow"] = endRow,
            ["startColumn"] = startColumn,
            ["endColumn"] = endColumn
        };

    private static JsonObject GetSheet(string workbookJson, string sheetId) =>
        JsonNode.Parse(workbookJson)!.AsObject()["sheets"]!.AsObject()[sheetId]!.AsObject();

    private static string? GetCellV(string workbookJson, string sheetId, int row, int col)
    {
        var sheet = GetSheet(workbookJson, sheetId);
        if (sheet["cellData"] is not JsonObject cellData)
            return null;
        if (cellData[row.ToString()] is not JsonObject rowObj)
            return null;
        if (rowObj[col.ToString()] is not JsonObject cell)
            return null;
        return cell["v"]?.GetValue<string>();
    }

    private static int? GetRowHeight(string workbookJson, string sheetId, int row)
    {
        var sheet = GetSheet(workbookJson, sheetId);
        if (sheet["rowData"] is not JsonObject rowData)
            return null;
        if (rowData[row.ToString()] is not JsonObject meta)
            return null;
        return meta["h"]?.GetValue<int>();
    }

    private static int? GetFreezeStartRow(string workbookJson, string sheetId) =>
        GetSheet(workbookJson, sheetId)["freeze"] is JsonObject freeze
            ? freeze["startRow"]?.GetValue<int>()
            : null;

    private static List<(int StartRow, int EndRow, int StartColumn, int EndColumn)> GetMerges(
        string workbookJson,
        string sheetId)
    {
        var sheet = GetSheet(workbookJson, sheetId);
        if (sheet["mergeData"] is not JsonArray merges)
            return [];

        return merges
            .Select(n => n!.AsObject())
            .Select(o => (
                o["startRow"]!.GetValue<int>(),
                o["endRow"]!.GetValue<int>(),
                o["startColumn"]!.GetValue<int>(),
                o["endColumn"]!.GetValue<int>()))
            .ToList();
    }
}
