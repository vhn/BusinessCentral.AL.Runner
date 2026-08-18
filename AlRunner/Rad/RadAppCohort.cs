namespace AlRunner.Rad;

/// <summary>
/// The apps of ONE bundle, as the cross-app rebind rules have to see them: which compiler
/// <c>AppId</c> belongs to which workspace identity.
///
/// <para><b>Why this exists at all.</b> A reference graph built during a compile knows only
/// what the compiler knows — a target symbol's <c>ContainingModule.AppId</c>, a Guid. The
/// workspaces that have to act on the edge are keyed by
/// <see cref="RadWorkspaceStore.IdentityOf"/>, which is NOT always that Guid: an
/// <c>AppGroup</c> with no <c>app.json</c> has a null <c>AppId</c> and keys as
/// <c>name:&lt;module&gt;</c>, while the compilation built for it is given
/// <c>DeterministicGuid(moduleName)</c>. Mapping the compiler's Guid to a workspace by
/// assuming they are the same thing therefore never matches for such a group — and it fails
/// SILENTLY, as zero retained edges, which is indistinguishable from "this app calls
/// nothing".</para>
///
/// <para><b>Why it is the bundle/app graph and not "a workspace happens to exist".</b> A
/// one-shot run has no <see cref="RadWorkspace"/> at all — the store is only enabled under
/// <c>--watch</c> — and yet it writes the baseline sidecar a later watch hydrates. Deciding
/// what to retain from live workspaces would write a sidecar with zero cross-app edges, so the
/// first watch over a cached tree would be exactly as stale as before, with nothing to show
/// for it. The app graph is available in both modes and says the same thing in both.</para>
///
/// <para><b>What is deliberately NOT in it: precompiled dependencies.</b> Only apps compiled
/// FROM SOURCE in this bundle can change between two watch cycles. A <c>.app</c> in
/// <c>.alpackages</c> cannot, and if one is replaced, <c>ReferenceSignature</c>'s
/// <c>ref|…|version|appId</c> line moves and the workspace invalidates wholesale. Retaining
/// edges into them would cost 70k–210k additional edges on npcore — a 2–4x sidecar — for
/// edges that can never be actionable.</para>
/// </summary>
public sealed class RadAppCohort
{
    private readonly Dictionary<Guid, string> _identityByAppId;

    private RadAppCohort(string bundleRoot, Dictionary<Guid, string> identityByAppId)
    {
        BundleRoot = bundleRoot;
        _identityByAppId = identityByAppId;
    }

    /// <summary>The bundle these apps were enumerated for — see <see cref="RadWorkspace.BundleRoot"/>.</summary>
    public string BundleRoot { get; }

    public int Count => _identityByAppId.Count;

    /// <summary>
    /// The workspace identity of the sibling source app the compiler calls
    /// <paramref name="compilerAppId"/>, or null when that app is not one of this bundle's —
    /// which is the answer for every precompiled dependency, the Base Application included.
    /// </summary>
    public string? IdentityOf(Guid compilerAppId) =>
        _identityByAppId.TryGetValue(compilerAppId, out var identity) ? identity : null;

    /// <summary>
    /// Build the cohort for one bundle from its app groups.
    /// </summary>
    /// <param name="apps">
    /// <c>(declared app.json id — null for a group without one, module name)</c> per app group,
    /// in any order.
    /// </param>
    public static RadAppCohort Build(
        string bundleRoot, IEnumerable<(Guid? AppId, string ModuleName)> apps)
    {
        var map = new Dictionary<Guid, string>();
        foreach (var (appId, moduleName) in apps)
        {
            // Both halves of the translation in one line: the key is what the COMPILER will
            // report for this app, the value is what the STORE keys its workspace by.
            var compilerAppId = BcCompiler.CompilationAppId(appId, moduleName);
            var identity = RadWorkspaceStore.IdentityOf(appId, moduleName);
            // Two groups whose compilations would be built with ONE AppId is not a bundle this
            // can answer questions about: whichever lost would have its edges silently retargeted
            // at the winner, and the loss is invisible downstream because "no edges" is also what
            // a correct answer looks like for an app that calls nothing.
            //
            // Duplicate app.json ids are rejected earlier (#1850) — but that check compares STORE
            // identities, and the collision left over is between a declared id and the
            // deterministic hash given to an app.json-less group, whose identities differ
            // (`<guid>` versus `name:<module>`) and which therefore passes it.
            if (map.TryGetValue(compilerAppId, out var existing)
                && !string.Equals(existing, identity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"two apps in this bundle compile under the same AppId {compilerAppId}: "
                    + $"'{existing}' and '{identity}'. The RAD reference graph cannot tell their "
                    + "objects apart, so cross-app rebinding would silently bind one app's callers "
                    + "to the other's surface. Give each app a distinct 'id' in its app.json.");
            }
            // Same app listed twice agrees with itself; nothing to disambiguate.
            map[compilerAppId] = identity;
        }
        return new RadAppCohort(
            Path.GetFullPath(bundleRoot).TrimEnd(Path.DirectorySeparatorChar), map);
    }
}
