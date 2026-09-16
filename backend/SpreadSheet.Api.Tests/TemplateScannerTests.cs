using System.Text.Json;
using SpreadSheet.Api.Services;

namespace SpreadSheet.Api.Tests;

public class TemplateScannerTests
{
    private readonly TemplateScanner _scanner = new();

    [Fact]
    public void Scan_SimpleField_AddsToFields()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "客户:{{CustomerName}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Contains("CustomerName", schema.Fields);
        Assert.Empty(schema.Loops);
    }

    [Fact]
    public void Scan_SameRowLoop_CreatesSingleRowLoopWithFields()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{Items.Name}}" },
                    ["2"] = new Dictionary<string, object> { ["v"] = "{{Items.Qty}}" },
                    ["3"] = new Dictionary<string, object> { ["v"] = "{{/Items}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Single(schema.Loops);
        var loop = schema.Loops[0];
        Assert.Equal("Items", loop.Name);
        Assert.Equal("Sheet1", loop.Sheet);
        Assert.Equal(1, loop.StartRow);
        Assert.Equal(1, loop.EndRow);
        Assert.Contains("Name", loop.Fields);
        Assert.Contains("Qty", loop.Fields);
        Assert.Empty(schema.Warnings);
    }

    [Fact]
    public void Scan_MismatchedLoopWithoutEnd_AddsWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{Items.Name}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.NotEmpty(schema.Warnings);
    }

    [Fact]
    public void Scan_MultiRowLoop_RecordsBlockRangeWithoutWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" }
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{Items.Name}}" }
                },
                ["3"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{/Items}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        var loop = Assert.Single(schema.Loops);
        Assert.Equal(1, loop.StartRow);
        Assert.Equal(3, loop.EndRow);
        Assert.Null(loop.ParentName);
        Assert.Equal(0, loop.Depth);
        Assert.Contains("Name", loop.Fields);
        Assert.Empty(schema.Warnings);
    }

    [Fact]
    public void Scan_NestedLoops_RecordsParentAndDepth()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Outer}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{Outer.Title}}" }
                },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Inner}}{{Inner.Code}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{/Inner}}{{/Outer}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Empty(schema.Warnings);

        var outer = schema.Loops.Single(l => l.Name == "Outer");
        Assert.Equal(0, outer.StartRow);
        Assert.Equal(1, outer.EndRow);
        Assert.Null(outer.ParentName);
        Assert.Equal(0, outer.Depth);
        Assert.Contains("Title", outer.Fields);

        var inner = schema.Loops.Single(l => l.Name == "Inner");
        Assert.Equal(1, inner.StartRow);
        Assert.Equal(1, inner.EndRow);
        Assert.Equal("Outer", inner.ParentName);
        Assert.Equal(1, inner.Depth);
        Assert.Contains("Code", inner.Fields);
    }

    [Fact]
    public void Scan_CrossingLoops_AddsCrossingWarningAndIgnoresTheCrossingBlock()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#A}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{#B}}" }
                },
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{/A}}" }
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{/B}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Contains(schema.Warnings, w => w.Contains("crosses", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("A", Assert.Single(schema.Loops).Name);
    }

    [Fact]
    public void Scan_DuplicateLoopName_AddsWarningAndKeepsFirstBlock()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{/Items}}" }
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{/Items}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Contains(schema.Warnings, w => w.Contains("Duplicate loop name", StringComparison.Ordinal));
        var loop = Assert.Single(schema.Loops);
        Assert.Equal(0, loop.StartRow);
    }

    [Fact]
    public void Scan_FormulaInsideLoop_AddsWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{Items.Name}}" },
                    ["2"] = new Dictionary<string, object> { ["f"] = "=SUM(A1:A2)" },
                    ["3"] = new Dictionary<string, object> { ["v"] = "{{/Items}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Contains(schema.Warnings, w => w.Contains("formula", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_MergeFullyInsideLoopBlock_NoWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{Items.Name}}" }
                },
                ["2"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{/Items}}" }
                }
            },
            mergeData:
            [
                new Dictionary<string, object>
                {
                    ["startRow"] = 1,
                    ["endRow"] = 2,
                    ["startColumn"] = 3,
                    ["endColumn"] = 3
                }
            ]);

        var schema = _scanner.Scan(workbook);

        Assert.Empty(schema.Warnings);
    }

    [Fact]
    public void Scan_MergeCrossingLoopBoundary_AddsWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["1"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Items}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{Items.Name}}" },
                    ["2"] = new Dictionary<string, object> { ["v"] = "{{/Items}}" }
                }
            },
            mergeData:
            [
                new Dictionary<string, object>
                {
                    ["startRow"] = 1,
                    ["endRow"] = 2,
                    ["startColumn"] = 0,
                    ["endColumn"] = 0
                }
            ]);

        var schema = _scanner.Scan(workbook);

        Assert.Contains(schema.Warnings, w =>
            w.Contains("crosses a loop boundary", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_DuplicateFieldMentions_FieldsAreUnique()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{CustomerName}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "Hello {{CustomerName}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Equal(1, schema.Fields.Count(f => f == "CustomerName"));
        Assert.Equal(schema.Fields.Distinct().Count(), schema.Fields.Count);
    }

    private static string Workbook(
        string sheet,
        string name,
        Dictionary<string, object> cellData,
        List<Dictionary<string, object>>? mergeData = null)
    {
        var sheetObj = new Dictionary<string, object>
        {
            ["id"] = sheet,
            ["name"] = name,
            ["cellData"] = cellData
        };
        if (mergeData is not null)
            sheetObj["mergeData"] = mergeData;

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
}
