namespace SpreadSheet.Api.Models;

public class TemplateSchema
{
    public List<string> Fields { get; set; } = [];
    public List<TemplateLoop> Loops { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class TemplateLoop
{
    public string Name { get; set; } = string.Empty;
    public string Sheet { get; set; } = string.Empty;
    public int StartRow { get; set; }
    public int EndRow { get; set; }
    public List<string> Fields { get; set; } = [];

    /// <summary>Enclosing loop name, or null for a top-level loop.</summary>
    public string? ParentName { get; set; }

    /// <summary>Nesting depth; 0 for a top-level loop.</summary>
    public int Depth { get; set; }
}
