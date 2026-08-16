namespace AlRunner.Rad;

/// <summary>
/// Compiler-independent AL object identity within one app.
///
/// <para>Most AL object kinds are identified by <c>(Kind, Id)</c> — ids are unique within a
/// kind, and that is what BC's own metadata, AllObj and the generated CLR type names are
/// keyed on. Some kinds have no id at all; for those <see cref="Name"/> is the identity, and
/// it is the discriminator EXACTLY when there is no id — an id-bearing object leaves
/// <see cref="Name"/> empty so its key, and therefore its equality, is unchanged.
/// <see cref="IsIdlessKind"/> is the list, and it is deliberately shorter than "every kind
/// AL gives no id to".</para>
///
/// <para>Getting this wrong was not a theoretical problem. A <c>profile</c> satisfies BC's
/// <c>ISymbolWithId</c> and then reports id 0, so before <see cref="Name"/> existed every
/// profile in an app keyed as <c>Profile:0</c>. An app with two of them produced two objects
/// with one key, which threw out of the RAD baseline snapshot and left the app with no
/// baseline — silently, because that throw is caught and logged.</para>
/// </summary>
public readonly record struct RadObjectKey(string Kind, int Id, string Name = "")
{
    public bool IsCodeunit => string.Equals(Kind, "Codeunit", StringComparison.Ordinal);

    /// <summary>
    /// Whether this object extends another one (tableextension, pageextension,
    /// enumextension, …). What such an object contributes — fields, controls, values —
    /// is only visible on its TARGET, which makes it the one kind the delta cannot strip
    /// from the packaged baseline. See BcCompiler.DeltaCompile.
    ///
    /// <para>This is a test on the KIND NAME, and <c>pagecustomization</c> is deliberately on
    /// the other side of it even though the compiler reports one as an
    /// <c>IApplicationObjectExtensionTypeSymbol</c> targeting a page. The exemption exists for
    /// objects that declare something they then reference through the target — the AL0132s a
    /// stripped tableextension produced against fields declared in its own file. A
    /// pagecustomization declares nothing bindable: a <c>modify()</c> names a control the
    /// target page already has, and the target page's symbol resolves from the baseline
    /// whether or not the customization is stripped alongside it. Measured both ways by
    /// RadIdlessObjectTests.ModifyingAnIdLessObject_LeavesOneBaselineCopy_CarryingTheNewShape,
    /// which fails if stripping breaks the bind AND if not stripping shadows the edit.</para>
    /// </summary>
    public bool IsExtension => Kind.EndsWith("Extension", StringComparison.Ordinal);

    /// <summary>
    /// Whether the object has no AL object id, and is therefore identified by name.
    /// </summary>
    public bool IsIdless => Id <= 0;

    /// <summary>
    /// Whether compiling this object produces a generated C# source.
    ///
    /// <para>The id-less kinds do not: an <c>interface</c> is a binding-time contract, and a
    /// <c>controladdin</c>, <c>profile</c>, <c>pagecustomization</c>, <c>profileextension</c>
    /// or <c>entitlement</c> is metadata. They contribute symbols to the module and nothing to
    /// the assembly, which is why a delta consisting only of id-less objects legitimately
    /// compiles no C# at all. The delta path compares the emitted count against this, so a
    /// kind misclassified here costs a fallback to a full compile — never a wrong module.</para>
    ///
    /// <para>A <c>permissionset</c> is the counter-example worth remembering: it looks like
    /// metadata and it DOES generate C#. It has a real object id, so it lands on the right
    /// side of this test by id rather than by anyone remembering to special-case it.</para>
    /// </summary>
    public bool EmitsCode => !IsIdless;

    /// <summary>
    /// The AL kinds that have no object id, so their name is their identity.
    ///
    /// <para>This is decided by KIND, not by whether a given representation happens to carry
    /// an id — because they disagree. A <c>profile</c> symbol implements
    /// <c>ISymbolWithId</c> and reports 0; a serialized <c>InterfaceDefinition</c> in a
    /// module definition reports a synthesized id (552062417 for one measured here) that no
    /// compiler symbol ever produces. Keying off "does it have an id?" therefore gives the
    /// same object two different keys depending on which side is asked, and the delta then
    /// fails to strip its own baseline copy — which surfaces as the pre-edit shape shadowing
    /// the edit (AL0582 against a member the source no longer declares).</para>
    ///
    /// <para>Being name-keyed is only half of being supported: for a MODIFIED object the
    /// module definition must also carry it, so a delta can strip the pre-edit copy that
    /// would otherwise shadow the supplied syntax. Five of these six kinds have an array in
    /// <c>ModuleDefinition</c> (<c>Interfaces</c>, <c>ControlAddIns</c>, <c>Profiles</c>,
    /// <c>PageCustomizations</c>, <c>ProfileExtensions</c>), and FOUR of them are stripped
    /// like any id-bearing object. <c>ProfileExtension</c> is not: the strip skips
    /// <see cref="IsExtension"/>, which tests the KIND NAME for the suffix "Extension", so a
    /// profileextension is carved out alongside the real extension kinds. That is not an
    /// oversight — RadIdlessObjectTests.ModifyingAnIdLessObject_LeavesOneBaselineCopy_CarryingTheNewShape
    /// covers it, and fails both if stripping breaks the bind and if leaving it in shadows
    /// the edit.</para>
    ///
    /// <para><c>Entitlement</c> is the exception, and it is safe for the opposite reason:
    /// there is no <c>Entitlements</c> array and no <c>EntitlementDefinition</c> type at all,
    /// so an entitlement has no serialized copy that could shadow anything, and nothing
    /// downstream can resolve one. It has no observable surface either, which is why
    /// <c>RadIdlessObjectTests</c> proves it against a cold compile of the same tree rather
    /// than against the baseline.</para>
    ///
    /// <para>What is NOT here: <c>dotnet</c> package declarations. They are not AL objects,
    /// they change what every object in the module can bind to, and a RAD object compilation
    /// carries no package declaration trees — the merge deliberately restores the previously
    /// committed <c>DotNetPackages</c>. Such a file still gets the whole module, but by a rule
    /// of its own rather than by declaring no object: a file that declares no object is now a
    /// delta costing no compiler work at all. See <see cref="RadFileDeclarations"/> and
    /// RadObjectDeltaTests.AFileDeclaringADotNetPackage_StillForcesAFullCompile.</para>
    /// </summary>
    public static bool IsIdlessKind(string kind) =>
        kind is "Interface" or "ControlAddIn" or "Profile"
             or "PageCustomization" or "ProfileExtension" or "Entitlement";

    /// <summary>
    /// Build a key from what a compiler symbol, a syntax declaration or a serialized module
    /// element reports, applying the identity rule in one place so all three agree.
    ///
    /// <para>The name is upper-cased because AL identifiers are case-insensitive: renaming
    /// an interface from <c>Contract</c> to <c>CONTRACT</c> is not a new object, and keying
    /// on the exact spelling classified it as one addition plus one removal instead of a
    /// modification. The display spelling is preserved on <c>RadObjectRef.Name</c>, which is
    /// what Microsoft's change model and every log line are given.</para>
    ///
    /// <para><c>id &lt;= 0</c> is a safety net rather than part of the rule: an id-bearing
    /// kind that unexpectedly reports no id gets a name to be told apart by, instead of
    /// colliding with every other such object on <c>(Kind, 0)</c> — the exact failure that
    /// cost apps with two profiles their baseline.</para>
    /// </summary>
    public static RadObjectKey For(string kind, int id, string? name) =>
        IsIdlessKind(kind) || id <= 0
            ? new(kind, 0, (name ?? string.Empty).ToUpperInvariant())
            : new(kind, id);

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
