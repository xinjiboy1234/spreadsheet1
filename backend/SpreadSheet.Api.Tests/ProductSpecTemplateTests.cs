using System.Text.Json;
using System.Text.Json.Nodes;
using SpreadSheet.Api.Services;

namespace SpreadSheet.Api.Tests;

/// <summary>
/// End-to-end check of the shipped product-specification template: a variable-height product
/// block with an inner option-code loop, whose Tags/Qty cells are merged down to the last row
/// of each product block.
/// </summary>
public class ProductSpecTemplateTests
{
    private const string SheetId = "sheet-1";

    [Fact]
    public void ProductSpecTemplate_ScanThenFill_ProducesStretchedMerges()
    {
        var workbookJson = ReadSeedFile("product-spec-template.workbook.json");
        var sampleJson = ReadSeedFile("product-spec-template.fill-sample.json");

        var schema = new TemplateScanner().Scan(workbookJson);
        Assert.Empty(schema.Warnings);

        using var data = JsonDocument.Parse(sampleJson);
        var result = new FillEngine().Fill(workbookJson, data.RootElement, schema);

        Assert.Empty(result.Warnings);
        Assert.DoesNotContain("{{", result.WorkbookJson, StringComparison.Ordinal);

        Assert.Equal("製品仕様書 DP-20260904-0009", CellV(result.WorkbookJson, 0, 1));
        Assert.Equal("Model / Part", CellV(result.WorkbookJson, 1, 1));

        // First product: description, part number, then nine option rows (output rows 2..12).
        Assert.StartsWith("マイクロモーションELITE", CellV(result.WorkbookJson, 2, 1), StringComparison.Ordinal);
        Assert.Equal("CMF010M323NRAMJZZZ", CellV(result.WorkbookJson, 3, 1));
        Assert.Equal("323", CellV(result.WorkbookJson, 4, 1));
        Assert.Equal("Z", CellV(result.WorkbookJson, 12, 1));

        // Second product starts right after and has four option rows (output rows 13..18).
        Assert.StartsWith("マイクロモーション 2700", CellV(result.WorkbookJson, 13, 1), StringComparison.Ordinal);
        Assert.Equal("2700RA1BBAEZZZ", CellV(result.WorkbookJson, 14, 1));
        Assert.Equal("R", CellV(result.WorkbookJson, 15, 1));
        Assert.Equal("B", CellV(result.WorkbookJson, 18, 1));

        var merges = Merges(result.WorkbookJson);

        // Tags and Qty stretch across each product block's actual height.
        Assert.Contains((2, 12, 4, 4), merges);
        Assert.Contains((2, 12, 5, 5), merges);
        Assert.Contains((13, 18, 4, 4), merges);
        Assert.Contains((13, 18, 5, 5), merges);

        // Description / part-number merges stay one row tall, once per product.
        Assert.Contains((2, 2, 1, 3), merges);
        Assert.Contains((3, 3, 1, 3), merges);
        Assert.Contains((13, 13, 1, 3), merges);
        Assert.Contains((14, 14, 1, 3), merges);

        // One option-description merge per rendered option row.
        var optionMerges = merges.Where(m => m.StartColumn == 2 && m.EndColumn == 3).ToList();
        Assert.Equal(13, optionMerges.Count);
        Assert.All(optionMerges, m => Assert.Equal(m.StartRow, m.EndRow));

        // Header merges are untouched and the frozen header row still resolves to row 2.
        Assert.Contains((0, 0, 1, 5), merges);
        Assert.Contains((1, 1, 1, 3), merges);
        Assert.Equal(2, FreezeStartRow(result.WorkbookJson));

        // Row heights follow the block: template row 4 (h 22) repeats for every option row.
        Assert.Equal(30, RowHeight(result.WorkbookJson, 0));
        Assert.Equal(24, RowHeight(result.WorkbookJson, 2));
        Assert.Equal(22, RowHeight(result.WorkbookJson, 4));
        Assert.Equal(22, RowHeight(result.WorkbookJson, 12));
        Assert.Equal(24, RowHeight(result.WorkbookJson, 13));
    }

    [Fact]
    public void ProductSpecTemplate_EmptyOptionList_ShrinksBlockToTwoRows()
    {
        var workbookJson = ReadSeedFile("product-spec-template.workbook.json");
        var schema = new TemplateScanner().Scan(workbookJson);

        using var data = JsonDocument.Parse(
            """
            {"OrderNo":"X","Products":[
              {"Desc":"d","PartNo":"p","Tags":"-","Qty":1,"Options":[]}]}
            """);

        var result = new FillEngine().Fill(workbookJson, data.RootElement, schema);

        Assert.Equal("d", CellV(result.WorkbookJson, 2, 1));
        Assert.Equal("p", CellV(result.WorkbookJson, 3, 1));
        Assert.Null(CellV(result.WorkbookJson, 4, 1));

        var merges = Merges(result.WorkbookJson);
        Assert.Contains((2, 3, 4, 4), merges);
        Assert.Contains((2, 3, 5, 5), merges);
        Assert.DoesNotContain(merges, m => m.StartColumn == 2 && m.EndColumn == 3);
    }

    private static string ReadSeedFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SpreadSheet.Api", "Seed", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate seed file '{fileName}'.");
    }

    private static JsonObject Sheet(string workbookJson) =>
        JsonNode.Parse(workbookJson)!.AsObject()["sheets"]!.AsObject()[SheetId]!.AsObject();

    private static string? CellV(string workbookJson, int row, int col)
    {
        if (Sheet(workbookJson)["cellData"] is not JsonObject cellData)
            return null;
        if (cellData[row.ToString()] is not JsonObject rowObj)
            return null;
        if (rowObj[col.ToString()] is not JsonObject cell)
            return null;
        return cell["v"]?.GetValue<string>();
    }

    private static int? RowHeight(string workbookJson, int row) =>
        Sheet(workbookJson)["rowData"] is JsonObject rowData &&
        rowData[row.ToString()] is JsonObject meta
            ? meta["h"]?.GetValue<int>()
            : null;

    private static int? FreezeStartRow(string workbookJson) =>
        Sheet(workbookJson)["freeze"] is JsonObject freeze
            ? freeze["startRow"]?.GetValue<int>()
            : null;

    private static List<(int StartRow, int EndRow, int StartColumn, int EndColumn)> Merges(string workbookJson)
    {
        if (Sheet(workbookJson)["mergeData"] is not JsonArray merges)
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
