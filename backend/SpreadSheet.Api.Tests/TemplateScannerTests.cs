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
    public void Scan_MultiRowLoop_AddsWarning()
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

        Assert.NotEmpty(schema.Warnings);
        Assert.Contains(schema.Warnings, w => w.Contains("multi", StringComparison.OrdinalIgnoreCase)
            || w.Contains("single-row", StringComparison.OrdinalIgnoreCase)
            || w.Contains("multiple rows", StringComparison.OrdinalIgnoreCase)
            || w.Contains("startRow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_NestedLoops_AddsNestedWarning()
    {
        var workbook = Workbook(
            sheet: "s1",
            name: "Sheet1",
            cellData: new Dictionary<string, object>
            {
                ["0"] = new Dictionary<string, object>
                {
                    ["0"] = new Dictionary<string, object> { ["v"] = "{{#Outer}}" },
                    ["1"] = new Dictionary<string, object> { ["v"] = "{{#Inner}}" },
                    ["2"] = new Dictionary<string, object> { ["v"] = "{{/Inner}}" },
                    ["3"] = new Dictionary<string, object> { ["v"] = "{{/Outer}}" }
                }
            });

        var schema = _scanner.Scan(workbook);

        Assert.Contains(schema.Warnings, w => w.Contains("nest", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_LoopOverlappingMerge_AddsMergeConflictWarning()
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

        Assert.Contains(schema.Warnings, w => w.Contains("merge", StringComparison.OrdinalIgnoreCase));
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
