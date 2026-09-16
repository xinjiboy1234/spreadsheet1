using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpreadSheet.Api.Data;
using SpreadSheet.Api.Data.Entities;
using SpreadSheet.Api.Services;

namespace SpreadSheet.Api.Seed;

public static class SeedData
{
    public static readonly Guid SalesOrderTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ProductSpecTemplateId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly SeedTemplate[] Templates =
    [
        new(SalesOrderTemplateId, "销售订单模板", "sales-order-template.workbook.json"),
        new(ProductSpecTemplateId, "製品仕様テンプレート", "product-spec-template.workbook.json")
    ];

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
        foreach (var template in Templates)
            await EnsureTemplateSeededAsync(db, scanner, env, template, ct);
    }

    private static async Task EnsureTemplateSeededAsync(
        AppDbContext db,
        TemplateScanner scanner,
        IHostEnvironment env,
        SeedTemplate template,
        CancellationToken ct)
    {
        if (await db.Documents.AsNoTracking().AnyAsync(d => d.Id == template.Id, ct))
            return;

        var workbookJson = await LoadWorkbookJsonAsync(env, template.FileName, ct);
        var schema = scanner.Scan(workbookJson);
        var schemaJson = JsonSerializer.Serialize(schema, SchemaJsonOptions);
        var now = DateTimeOffset.UtcNow;

        var doc = new Document
        {
            Id = template.Id,
            Title = template.Title,
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

    internal static async Task<string> LoadWorkbookJsonAsync(
        IHostEnvironment env,
        string fileName,
        CancellationToken ct = default)
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "Seed", fileName),
            Path.Combine(AppContext.BaseDirectory, "Seed", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return await File.ReadAllTextAsync(path, ct);
        }

        throw new FileNotFoundException(
            $"Seed workbook not found. Tried: {string.Join("; ", candidates)}");
    }

    private sealed record SeedTemplate(Guid Id, string Title, string FileName);
}
