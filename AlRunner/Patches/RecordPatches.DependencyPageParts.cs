namespace AlRunner.Patches;

internal enum TestPagePartLinkKind
{
    Field,
    Const,
    Filter,
}

internal sealed record TestPagePartLink(
    int PartFieldNo,
    TestPagePartLinkKind Kind,
    int ParentFieldNo = 0,
    string? Value = null);

public static partial class RecordPatches
{
    internal static BcAppSymbolCache.PagePartSymbol? TryGetDependencyPagePartSymbol(
        int pageId,
        int controlId)
        => TryGetDependencyPageSymbol(pageId)?.Parts?.FirstOrDefault(part => part.Id == controlId);

    internal static TestPagePartLink[] ResolveDependencyPagePartLinks(
        int parentPageId,
        BcAppSymbolCache.PagePartSymbol part)
    {
        if (string.IsNullOrWhiteSpace(part.SubPageLink))
            return [];

        var parentTableId = ResolveSourceTableIdForAnyPage(parentPageId);
        var partTableId = ResolveSourceTableIdForAnyPage(part.PagePartId);
        if (parentTableId == 0 || partTableId == 0)
            throw new AlRunner.Infrastructure.RunnerOutOfScopeException(
                $"TestPage part {part.Id} (page {parentPageId})",
                "testpage-part-link — the parent or part page has no resolvable SourceTable, "
                + "so its SubPageLink field names cannot be bound. See docs/scope.md");

        TryPopulateParsedTableFromBcApps(parentTableId);
        TryPopulateParsedTableFromBcApps(partTableId);
        return ParseSubPageLinkText(
            part.SubPageLink,
            BuildFieldNameToNoMap(partTableId),
            BuildFieldNameToNoMap(parentTableId));
    }

    internal static TestPagePartLink[] ParseSubPageLinkText(
        string text,
        IReadOnlyDictionary<string, int> partFields,
        IReadOnlyDictionary<string, int> parentFields)
    {
        var result = new List<TestPagePartLink>();
        foreach (var rawClause in SplitSubPageLinkClauses(text))
        {
            var clause = rawClause.Trim();
            if (clause.Length == 0) continue;

            var equals = IndexOfSubPageLinkEquals(clause);
            if (equals <= 0)
                throw InvalidSubPageLink(text, $"clause '{clause}' has no top-level '='");

            var partFieldName = Unquote(clause[..equals].Trim());
            if (!partFields.TryGetValue(partFieldName, out var partFieldNo))
                throw InvalidSubPageLink(text, $"part field '{partFieldName}' was not found");

            var rhs = clause[(equals + 1)..].Trim();
            var openParen = rhs.IndexOf('(');
            if (openParen <= 0 || !rhs.EndsWith(')'))
                throw InvalidSubPageLink(text, $"right-hand side '{rhs}' is not field(...), const(...), or filter(...)");

            var kind = rhs[..openParen].Trim();
            var value = rhs[(openParen + 1)..^1].Trim();
            if (kind.Equals("field", StringComparison.OrdinalIgnoreCase))
            {
                var parentFieldName = Unquote(value);
                if (!parentFields.TryGetValue(parentFieldName, out var parentFieldNo))
                    throw InvalidSubPageLink(text, $"parent field '{parentFieldName}' was not found");
                result.Add(new TestPagePartLink(partFieldNo, TestPagePartLinkKind.Field, parentFieldNo));
            }
            else if (kind.Equals("const", StringComparison.OrdinalIgnoreCase))
                result.Add(new TestPagePartLink(partFieldNo, TestPagePartLinkKind.Const, Value: value));
            else if (kind.Equals("filter", StringComparison.OrdinalIgnoreCase))
                result.Add(new TestPagePartLink(partFieldNo, TestPagePartLinkKind.Filter, Value: value));
            else
                throw InvalidSubPageLink(text, $"unsupported link kind '{kind}'");
        }
        return result.ToArray();
    }

    private static AlRunner.Infrastructure.RunnerOutOfScopeException InvalidSubPageLink(
        string link,
        string reason)
        => new(
            $"TestPage SubPageLink '{link}'",
            $"testpage-part-link — {reason}. See docs/scope.md");

    private static IEnumerable<string> SplitSubPageLinkClauses(string text)
    {
        var start = 0;
        var depth = 0;
        var quotedIdentifier = false;
        var quotedString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (current == '"' && !quotedString)
            {
                if (quotedIdentifier && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                quotedIdentifier = !quotedIdentifier;
                continue;
            }
            if (current == '\'' && !quotedIdentifier)
            {
                if (quotedString && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                    continue;
                }
                quotedString = !quotedString;
                continue;
            }
            if (quotedIdentifier || quotedString) continue;
            if (current == '(') depth++;
            else if (current == ')' && depth > 0) depth--;
            else if (current == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        yield return text[start..];
    }

    private static int IndexOfSubPageLinkEquals(string text)
    {
        var quotedIdentifier = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                if (quotedIdentifier && i + 1 < text.Length && text[i + 1] == '"')
                {
                    i++;
                    continue;
                }
                quotedIdentifier = !quotedIdentifier;
            }
            else if (text[i] == '=' && !quotedIdentifier)
                return i;
        }
        return -1;
    }
}
