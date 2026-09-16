using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SpreadSheet.Api.Data;
using SpreadSheet.Api.Seed;
using SpreadSheet.Api.Services;
using System.Text.Json;

namespace SpreadSheet.Api.Tests;

public class SeedDataTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _connectionString;
    private readonly IHostEnvironment _env;

    public SeedDataTests()
    {
        _connectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"ss-seed-{Guid.NewGuid():N}.db")}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _env = new TestHostEnvironment(FindApiContentRoot());
    }

    public void Dispose()
    {
        _db.Dispose();
        try
        {
            var path = _connectionString.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    [Fact]
    public async Task EnsureSeeded_InsertsSalesOrderTemplateWithExpectedSchema()
    {
        await SeedData.EnsureSeededAsync(_db, new TemplateScanner(), _env);

        var doc = await _db.Documents
            .Include(d => d.CurrentVersion)
            .SingleAsync(d => d.Id == SeedData.SalesOrderTemplateId);

        Assert.Equal("销售订单模板", doc.Title);
        Assert.NotNull(doc.CurrentVersion);
        Assert.Equal(1, doc.CurrentVersion!.VersionNo);
        Assert.Contains("{{CustomerName}}", doc.CurrentVersion.WorkbookJson, StringComparison.Ordinal);
        Assert.Contains("{{OrderDate}}", doc.CurrentVersion.WorkbookJson, StringComparison.Ordinal);
        Assert.Contains("{{Items.Amount}}", doc.CurrentVersion.WorkbookJson, StringComparison.Ordinal);

        using var schemaDoc = JsonDocument.Parse(doc.CurrentVersion.TemplateSchemaJson);
        var root = schemaDoc.RootElement;
        var fields = root.GetProperty("fields").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("CustomerName", fields);
        Assert.Contains("OrderDate", fields);

        var loop = root.GetProperty("loops").EnumerateArray().Single();
        Assert.Equal("Items", loop.GetProperty("name").GetString());
        Assert.Equal(2, loop.GetProperty("startRow").GetInt32());
        Assert.Equal(2, loop.GetProperty("endRow").GetInt32());
        var loopFields = loop.GetProperty("fields").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Name", loopFields);
        Assert.Contains("Qty", loopFields);
        Assert.Contains("Amount", loopFields);
    }

    [Fact]
    public async Task EnsureSeeded_InsertsProductSpecTemplateWithNestedLoops()
    {
        await SeedData.EnsureSeededAsync(_db, new TemplateScanner(), _env);

        var doc = await _db.Documents
            .Include(d => d.CurrentVersion)
            .SingleAsync(d => d.Id == SeedData.ProductSpecTemplateId);

        using var schemaDoc = JsonDocument.Parse(doc.CurrentVersion!.TemplateSchemaJson);
        var root = schemaDoc.RootElement;
        Assert.Empty(root.GetProperty("warnings").EnumerateArray());

        var loops = root.GetProperty("loops").EnumerateArray().ToList();
        Assert.Equal(2, loops.Count);

        var products = loops.Single(l => l.GetProperty("name").GetString() == "Products");
        Assert.Equal(2, products.GetProperty("startRow").GetInt32());
        Assert.Equal(4, products.GetProperty("endRow").GetInt32());
        Assert.Equal(0, products.GetProperty("depth").GetInt32());

        var options = loops.Single(l => l.GetProperty("name").GetString() == "Options");
        Assert.Equal(4, options.GetProperty("startRow").GetInt32());
        Assert.Equal(4, options.GetProperty("endRow").GetInt32());
        Assert.Equal("Products", options.GetProperty("parentName").GetString());
        Assert.Equal(1, options.GetProperty("depth").GetInt32());
    }

    [Fact]
    public async Task EnsureSeeded_IsIdempotent()
    {
        var scanner = new TemplateScanner();
        await SeedData.EnsureSeededAsync(_db, scanner, _env);
        await SeedData.EnsureSeededAsync(_db, scanner, _env);

        Assert.Equal(2, await _db.Documents.CountAsync());
        Assert.Equal(2, await _db.DocumentVersions.CountAsync());
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SpreadSheet.Api");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "Seed", "sales-order-template.workbook.json")))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SpreadSheet.Api content root with seed workbook.");
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SpreadSheet.Api.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
