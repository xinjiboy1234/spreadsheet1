using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpreadSheet.Api.Data;
using SpreadSheet.Api.Models;
using SpreadSheet.Api.Services;

namespace SpreadSheet.Api.Tests;

public class DocumentServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly DocumentService _sut;
    private readonly string _connectionString;

    private static readonly string MinimalWorkbook = """
        {
          "id": "wb1",
          "sheetOrder": ["s1"],
          "sheets": {
            "s1": {
              "id": "s1",
              "name": "Sheet1",
              "cellData": {
                "0": { "0": { "v": "客户:{{CustomerName}}" } },
                "1": {
                  "0": { "v": "{{#Items}}" },
                  "1": { "v": "{{Items.Name}}" },
                  "2": { "v": "{{Items.Qty}}" },
                  "3": { "v": "{{/Items}}" }
                }
              }
            }
          }
        }
        """;

    private static readonly string SampleFillData = """
        {
          "CustomerName": "张三公司",
          "OrderDate": "2026-09-15",
          "Items": [
            { "Name": "零件A", "Qty": 2, "Amount": 100 },
            { "Name": "零件B", "Qty": 5, "Amount": 250 }
          ]
        }
        """;

    public DocumentServiceTests()
    {
        _connectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"ss-doc-{Guid.NewGuid():N}.db")}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new DocumentService(_db, new TemplateScanner(), new FillEngine());
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
    public async Task Create_RequiresWorkbookJson()
    {
        var ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.CreateAsync(new CreateDocumentRequest { Title = "t", WorkbookJson = null! }));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("workbookJson", ex.Message, StringComparison.OrdinalIgnoreCase);

        ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.CreateAsync(new CreateDocumentRequest { WorkbookJson = "   " }));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Create_DefaultsTitle_AndScansSchema()
    {
        var doc = await _sut.CreateAsync(new CreateDocumentRequest { WorkbookJson = MinimalWorkbook });

        Assert.Equal("未命名文档", doc.Title);
        Assert.Equal(1, doc.VersionNo);
        Assert.False(string.IsNullOrWhiteSpace(doc.WorkbookJson));
        Assert.NotNull(doc.Schema);
        Assert.Contains("CustomerName", doc.Schema!.Fields);
        Assert.Contains(doc.Schema.Loops, l => l.Name == "Items");
    }

    [Fact]
    public async Task List_ReturnsSummaries()
    {
        await _sut.CreateAsync(new CreateDocumentRequest { Title = "A", WorkbookJson = MinimalWorkbook });
        await _sut.CreateAsync(new CreateDocumentRequest { Title = "B", WorkbookJson = MinimalWorkbook });

        var list = await _sut.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.All(list, x => Assert.Equal(1, x.CurrentVersionNo));
        Assert.Contains(list, x => x.Title == "A");
        Assert.Contains(list, x => x.Title == "B");
    }

    [Fact]
    public async Task Get_ReturnsCurrentVersion()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest
        {
            Title = "订单",
            WorkbookJson = MinimalWorkbook
        });

        var got = await _sut.GetAsync(created.Id);

        Assert.Equal(created.Id, got.Id);
        Assert.Equal("订单", got.Title);
        Assert.Equal(created.CurrentVersionId, got.CurrentVersionId);
        Assert.Equal(1, got.VersionNo);
        Assert.Contains("CustomerName", got.WorkbookJson);
    }

    [Fact]
    public async Task Put_BumpsVersionNo_AndRescansSchema()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest
        {
            Title = "订单",
            WorkbookJson = MinimalWorkbook
        });

        var updatedWorkbook = MinimalWorkbook.Replace("{{CustomerName}}", "{{CustomerName}}{{OrderDate}}", StringComparison.Ordinal);
        var updated = await _sut.UpdateAsync(created.Id, new UpdateDocumentRequest
        {
            WorkbookJson = updatedWorkbook,
            Title = "订单v2",
            Remark = "改字段"
        });

        Assert.Equal(2, updated.VersionNo);
        Assert.Equal("订单v2", updated.Title);
        Assert.NotEqual(created.CurrentVersionId, updated.CurrentVersionId);
        Assert.Contains("OrderDate", updated.Schema!.Fields);

        var versions = await _sut.ListVersionsAsync(created.Id);
        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions.Max(v => v.VersionNo));
    }

    [Fact]
    public async Task ListVersions_OmitsWorkbook()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest { WorkbookJson = MinimalWorkbook });
        await _sut.UpdateAsync(created.Id, new UpdateDocumentRequest
        {
            WorkbookJson = MinimalWorkbook,
            Remark = "v2"
        });

        var versions = await _sut.ListVersionsAsync(created.Id);

        Assert.Equal(2, versions.Count);
        Assert.All(versions, v =>
        {
            Assert.NotEqual(Guid.Empty, v.Id);
            Assert.True(v.VersionNo >= 1);
            Assert.NotEqual(default, v.CreatedAt);
        });
        // DTO has no WorkbookJson property — covered by type shape
        Assert.Null(typeof(DocumentVersionSummaryDto).GetProperty("WorkbookJson"));
    }

    [Fact]
    public async Task GetVersion_ReturnsWorkbookAndSchema()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest { WorkbookJson = MinimalWorkbook });
        var versions = await _sut.ListVersionsAsync(created.Id);
        var v1 = versions.Single();

        var detail = await _sut.GetVersionAsync(created.Id, v1.Id);

        Assert.Equal(v1.Id, detail.Id);
        Assert.Equal(1, detail.VersionNo);
        Assert.Contains("CustomerName", detail.WorkbookJson);
        Assert.NotNull(detail.Schema);
        Assert.Contains("CustomerName", detail.Schema!.Fields);
    }

    [Fact]
    public async Task GetSchema_ReturnsPersistedCurrentSchema()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest { WorkbookJson = MinimalWorkbook });

        var schema = await _sut.GetSchemaAsync(created.Id);

        Assert.Contains("CustomerName", schema.Fields);
        Assert.Contains(schema.Loops, l => l.Name == "Items");
    }

    [Fact]
    public async Task Fill_ReturnsWorkbookAndWarnings_WithoutWritingDb()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest
        {
            Title = "模板",
            WorkbookJson = MinimalWorkbook
        });
        var versionCountBefore = await _db.DocumentVersions.CountAsync();
        var docCountBefore = await _db.Documents.CountAsync();

        using var data = JsonDocument.Parse("""{"Items":[{"Name":"A","Qty":1}]}""");
        var fill = await _sut.FillAsync(created.Id, data.RootElement);

        Assert.False(string.IsNullOrWhiteSpace(fill.WorkbookJson));
        Assert.Contains(fill.Warnings, w => w.Contains("CustomerName", StringComparison.Ordinal));
        Assert.NotNull(fill.Schema);
        Assert.Contains("CustomerName", fill.Schema!.Fields); // pre-fill schema
        Assert.Equal(versionCountBefore, await _db.DocumentVersions.CountAsync());
        Assert.Equal(docCountBefore, await _db.Documents.CountAsync());

        var still = await _sut.GetAsync(created.Id);
        Assert.Contains("{{CustomerName}}", still.WorkbookJson);
    }

    [Fact]
    public async Task FillSave_CreatesNewDocument_WithRescannedSchema()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest
        {
            Title = "销售订单",
            WorkbookJson = MinimalWorkbook
        });

        using var data = JsonDocument.Parse(SampleFillData);
        var result = await _sut.FillSaveAsync(created.Id, new FillSaveRequest
        {
            Data = data.RootElement.Clone()
        });

        Assert.NotEqual(created.Id, result.Id);

        var original = await _sut.GetAsync(created.Id);
        Assert.Equal(1, original.VersionNo);
        Assert.Contains("{{CustomerName}}", original.WorkbookJson);

        var filled = await _sut.GetAsync(result.Id);
        Assert.Equal("销售订单-填充", filled.Title);
        Assert.Equal(1, filled.VersionNo);
        using (var wb = JsonDocument.Parse(filled.WorkbookJson))
        {
            var cell = wb.RootElement.GetProperty("sheets").GetProperty("s1")
                .GetProperty("cellData").GetProperty("0").GetProperty("0").GetProperty("v").GetString();
            Assert.Equal("客户:张三公司", cell);
        }
        Assert.DoesNotContain("{{CustomerName}}", filled.WorkbookJson);
        // re-scanned schema on filled workbook — placeholders gone
        Assert.DoesNotContain("CustomerName", filled.Schema!.Fields);
    }

    [Fact]
    public async Task FillSave_CustomTitle()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest
        {
            Title = "原标题",
            WorkbookJson = MinimalWorkbook
        });

        using var data = JsonDocument.Parse(SampleFillData);
        var result = await _sut.FillSaveAsync(created.Id, new FillSaveRequest
        {
            Data = data.RootElement.Clone(),
            Title = "自定义标题"
        });

        var filled = await _sut.GetAsync(result.Id);
        Assert.Equal("自定义标题", filled.Title);
    }

    [Fact]
    public async Task MissingDocument_Throws404()
    {
        var missing = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<DocumentServiceException>(() => _sut.GetAsync(missing));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("文档不存在", ex.Message);

        ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.UpdateAsync(missing, new UpdateDocumentRequest { WorkbookJson = MinimalWorkbook }));
        Assert.Equal(404, ex.StatusCode);

        ex = await Assert.ThrowsAsync<DocumentServiceException>(() => _sut.ListVersionsAsync(missing));
        Assert.Equal(404, ex.StatusCode);

        ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.GetVersionAsync(missing, Guid.NewGuid()));
        Assert.Equal(404, ex.StatusCode);

        ex = await Assert.ThrowsAsync<DocumentServiceException>(() => _sut.GetSchemaAsync(missing));
        Assert.Equal(404, ex.StatusCode);

        using var data = JsonDocument.Parse("{}");
        ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.FillAsync(missing, data.RootElement));
        Assert.Equal(404, ex.StatusCode);

        ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.FillSaveAsync(missing, new FillSaveRequest { Data = data.RootElement.Clone() }));
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task GetVersion_WrongId_Throws404()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest { WorkbookJson = MinimalWorkbook });

        var ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.GetVersionAsync(created.Id, Guid.NewGuid()));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("文档不存在", ex.Message);
    }

    [Fact]
    public async Task Update_RequiresWorkbookJson()
    {
        var created = await _sut.CreateAsync(new CreateDocumentRequest { WorkbookJson = MinimalWorkbook });

        var ex = await Assert.ThrowsAsync<DocumentServiceException>(() =>
            _sut.UpdateAsync(created.Id, new UpdateDocumentRequest { WorkbookJson = "" }));
        Assert.Equal(400, ex.StatusCode);
    }
}
