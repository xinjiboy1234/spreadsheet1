using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpreadSheet.Api.Data;
using SpreadSheet.Api.Data.Entities;
using SpreadSheet.Api.Services;

namespace SpreadSheet.Api.Seed;

public static class SeedData
{
    public static readonly Guid SalesOrderTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string SalesOrderTitle = "销售订单模板";
    private const string WorkbookFileName = "sales-order-template.workbook.json";

    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task EnsureSeededAsync(
        AppDbContext db,
        TemplateScanner scanner,
        IHostEnvironment env,
        CancellationToken ct = default)
    {
        if (await db.Documents.AsNoTracking().AnyAsync(d => d.Id == SalesOrderTemplateId, ct))
            return;

        var workbookJson = await LoadWorkbookJsonAsync(env, ct);
        var schema = scanner.Scan(workbookJson);
        var schemaJson = JsonSerializer.Serialize(schema, SchemaJsonOptions);
        var now = DateTimeOffset.UtcNow;

        var doc = new Document
        {
            Id = SalesOrderTemplateId,
            Title = SalesOrderTitle,
            CreatedAt = now,
            UpdatedAt = now,
            CurrentVersionId = null
        };

        var version = new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            VersionNo = 1,
            Remark = "seed",
            WorkbookJson = workbookJson,
            TemplateSchemaJson = schemaJson,
            CreatedAt = now
        };

        db.Documents.Add(doc);
        db.DocumentVersions.Add(version);
        await db.SaveChangesAsync(ct);

        doc.CurrentVersionId = version.Id;
        await db.SaveChangesAsync(ct);
    }

    internal static async Task<string> LoadWorkbookJsonAsync(IHostEnvironment env, CancellationToken ct = default)
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "Seed", WorkbookFileName),
            Path.Combine(AppContext.BaseDirectory, "Seed", WorkbookFileName),
            Path.Combine(AppContext.BaseDirectory, WorkbookFileName)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return await File.ReadAllTextAsync(path, ct);
        }

        throw new FileNotFoundException(
            $"Seed workbook not found. Tried: {string.Join("; ", candidates)}");
    }
}
