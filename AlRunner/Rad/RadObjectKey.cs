namespace AlRunner.Rad;

/// <summary>Compiler-independent AL object identity within one app.</summary>
public readonly record struct RadObjectKey(string Kind, int Id)
{
    public bool IsCodeunit => string.Equals(Kind, "Codeunit", StringComparison.Ordinal);

    /// <summary>The generated top-level CLR type owned by this AL object, when it has one.</summary>
    public string? ClrTypeName => Kind switch
    {
        "Table" => $"Record{Id}",
        "TableExtension" => $"TableExtension{Id}",
        "Codeunit" => $"Codeunit{Id}",
        "Page" => $"Page{Id}",
        "PageExtension" => $"PageExtension{Id}",
        "Report" => $"Report{Id}",
        "ReportExtension" => $"ReportExtension{Id}",
        "Query" => $"Query{Id}",
        "XmlPort" => $"XmlPort{Id}",
        _ => null,
    };
}
