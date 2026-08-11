// CallSiteArgWrap — fixes the residual call-site ByRef gap in BC's emitter.
//
// BC's `Compilation.Emit` wraps parameter declarations in `ByRef<T>` natively
// (codeanalysis.cs:342854 EmitParameterType). It also wraps most argument
// expressions at call sites (codeanalysis.cs:264213 EmitFieldRefByRefArgument).
// But it misses some — e.g. `dict.ALGet(K, fieldOfHandleT)` where the field's
// static type is `T` (a Handle subclass) and the callee expects `ByRef<T>`.
//
// Strategy:
//   1. Take the CS1503 diagnostics from a compile that ALREADY ran.
//   2. Filter to "cannot convert from 'T' to 'ByRef<T>'" shape.
//   3. Rewrite the offending argument expression `expr` to
//      `new ByRef<T>(() => expr, v => expr = v)`.
//   4. Return the rewritten trees so BcAssembler.Compile can emit cleanly.
//
// The diagnostics come from the REAL emit, not a throw-away bind. BcAssembler used
// to run this pass unconditionally before emitting, which meant a full Roslyn bind of
// every tree on every compile whether or not any ByRef gap existed — measured at
// 79.8 s over 6,947 trees producing 139,495 diagnostics and **0 rewrites** on a
// 7,000-object app, then binding the identical trees a second time to emit. Emitting
// first makes the no-gap case free and the rare real case cost one extra emit.
//
// This adds NO type renames or identifier rewrites — only argument expressions
// are wrapped, so the resulting IL stays binary-compatible with Microsoft's
// pre-compiled R2R DLLs (the §A "no rewriting" rule).
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AlRunner.Rewriters;

public static class CallSiteArgWrap
{
    private static readonly Regex _byRefMessage = new(
        @"cannot convert from '(?<from>[^']+)' to '(?<to>(?:[\w\.]+\.)?ByRef<[^']+>)'",
        RegexOptions.Compiled);

    /// <summary>
    /// Rewrite the ByRef argument gaps named by <paramref name="diagnostics"/> (the
    /// diagnostics of a compile that already ran over <paramref name="trees"/>).
    /// Returns the rewritten tree list, or <c>null</c> when there was nothing to do —
    /// meaning the caller's failure is a genuine compile error, not a ByRef gap.
    /// </summary>
    public static IReadOnlyList<SyntaxTree>? TryRewrite(
        IReadOnlyList<SyntaxTree> trees,
        IEnumerable<Diagnostic> diagnostics)
    {
        var targets = new List<(SyntaxTree Tree, TextSpan Span, string ByRefType)>();
        foreach (var d in diagnostics)
        {
            if (d.Severity != DiagnosticSeverity.Error) continue;
            if (d.Id != "CS1503") continue;
            var m = _byRefMessage.Match(d.GetMessage());
            if (!m.Success) continue;
            if (d.Location.SourceTree == null) continue;
            targets.Add((d.Location.SourceTree, d.Location.SourceSpan, m.Groups["to"].Value));
        }
        if (targets.Count == 0) return null;

        var byTree = targets
            .GroupBy(t => t.Tree)
            .ToDictionary(g => g.Key, g => g.ToList());

        var current = trees.ToList();
        bool changed = false;
        for (int i = 0; i < current.Count; i++)
        {
            if (!byTree.TryGetValue(current[i], out var spans)) continue;
            var oldRoot = current[i].GetRoot();
            var rewriter = new ArgRewriter(spans);
            var newRoot = rewriter.Visit(oldRoot);
            if (rewriter.Rewrote == 0) continue;
            current[i] = current[i].WithRootAndOptions(newRoot, current[i].Options);
            changed = true;
        }
        return changed ? current : null;
    }

    private sealed class ArgRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<TextSpan, string> _spanToType;
        public int Rewrote;

        public ArgRewriter(IEnumerable<(SyntaxTree Tree, TextSpan Span, string ByRefType)> targets)
        {
            _spanToType = targets.ToDictionary(t => t.Span, t => t.ByRefType);
        }

        public override SyntaxNode? VisitArgument(ArgumentSyntax node)
        {
            // The CS1503 location is on the argument's expression, not the ArgumentSyntax
            // node itself. Match by the expression's span overlapping the diagnostic span.
            var expr = node.Expression;
            string? byRefType = null;
            foreach (var kv in _spanToType)
            {
                if (kv.Key.OverlapsWith(expr.Span) || expr.Span.OverlapsWith(kv.Key))
                {
                    byRefType = kv.Value;
                    break;
                }
            }
            if (byRefType == null) return base.VisitArgument(node);

            // Build:  new <ByRefType>(() => <expr>, v => <expr> = v)
            // <ByRefType> is "Microsoft.Dynamics.Nav.Runtime.ByRef<T>" or similar.
            var exprText = expr.ToString();
            var wrapped = SyntaxFactory.ParseExpression(
                $"new {byRefType}(() => {exprText}, v => {exprText} = v)")
                .WithTriviaFrom(expr);
            Rewrote++;
            return node.WithExpression(wrapped);
        }
    }
}
