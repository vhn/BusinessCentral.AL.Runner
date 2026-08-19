// RadCrossAppBundleScopeTests — the cross-app rebind must never cross a BUNDLE boundary.
//
// The hazard
// ----------
// RadWorkspaceStore's map is process-wide and never cleared, and its key is
// `identity + "|" + sourceRoot` (RadWorkspace.cs, RadWorkspaceStore.For). So the SAME app
// id is legitimately present twice — two checkouts of one repo watched from one resident
// process, a test host that has run several bundle scenarios, a monorepo whose bundles share
// a package cache. Identity alone therefore does not name one app; it names an app in a
// checkout.
//
// A cross-app query that fanned out over the whole store would hand one checkout's consumer a
// producer belonging to the other. The consequence is not a crash: the consumer would be
// re-emitted against member ids that moved in a tree it does not compile from, or — worse for
// the developer sitting in front of it — the widening it actually needed would be attributed
// to the wrong producer and its files re-emitted for the wrong reason. RadWorkspaceStore.InBundle
// is what prevents it, and it is one `.Where` clause: exactly the kind of scoping that reads as
// defensive noise until something depends on it.
//
// What makes this test prove something
// ------------------------------------
// Both directions, over the identical fixture:
//
//   * a surface move published by the OTHER bundle's producer — same identity, same module
//     name, different bundle root — yields NOTHING for this consumer, and
//   * the same move published by THIS bundle's producer yields exactly one rebind, naming that
//     producer instance and the consumer's own object key.
//
// Without the second half "returns nothing" would be satisfied by a query that always returns
// nothing. The producers are compared by instance (Assert.Same), not by identity, because
// identity is precisely what the two share.

using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

public sealed class RadCrossAppBundleScopeTests
{
    // Outside every fixture's range so a stray key from another suite cannot collide.
    private static readonly RadObjectKey ProducerSurface = new("Codeunit", 88101);
    private static readonly RadObjectKey ConsumerCaller = new("Codeunit", 88201);

    /// <summary>
    /// Two checkouts of one app, watched from one process. A surface move in checkout A must
    /// not rebind checkout B's consumer, and a surface move in checkout B must.
    /// </summary>
    [Fact]
    public void ASurfaceMovePublishedInAnotherBundle_RebindsNothingInThisOne()
    {
        var root = NewRoot();
        try
        {
            var producerIdentity = Guid.NewGuid();
            var bundleA = Path.Combine(root, "checkout-a");
            var bundleB = Path.Combine(root, "checkout-b");

            // The premise, pinned rather than assumed: the store really does admit one app id
            // twice. If For() ever collapsed these two, the guard below would be testing nothing.
            var producerInA = RadWorkspaceStore.For(
                "Delta Lib", producerIdentity, Path.Combine(bundleA, "Lib"), bundleA);
            var producerInB = RadWorkspaceStore.For(
                "Delta Lib", producerIdentity, Path.Combine(bundleB, "Lib"), bundleB);
            Assert.NotSame(producerInA, producerInB);
            Assert.Equal(producerInA.Identity, producerInB.Identity);
            Assert.Equal(producerIdentity.ToString("N"), producerInB.Identity);
            Assert.NotEqual(producerInA.BundleRoot, producerInB.BundleRoot);

            var consumerInB = RadWorkspaceStore.For(
                "Delta Bridge", Guid.NewGuid(), Path.Combine(bundleB, "Bridge"), bundleB);
            CommitCrossAppEdge(consumerInB, ConsumerCaller, producerInB.Identity, ProducerSurface);

            // Nothing published anywhere yet.
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumerInB));

            // The OTHER checkout re-emits the very surface this consumer calls. Same identity,
            // same module name, same key — everything except the bundle.
            producerInA.PublishSurfaceMoves([ProducerSurface], fullRebuild: false);
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumerInB));

            // …and the same move in THIS checkout is picked up, so the silence above is scoping
            // and not a query that answers nothing.
            producerInB.PublishSurfaceMoves([ProducerSurface], fullRebuild: false);
            var rebind = Assert.Single(RadWorkspaceStore.PendingCrossAppRebinds(consumerInB));
            Assert.Same(producerInB, rebind.Producer);
            Assert.False(rebind.Everything);
            Assert.Equal([ConsumerCaller], rebind.Users);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The same scoping for the coarsest signal there is. A full rebuild broadcasts "assume
    /// everything moved" (<see cref="RadWorkspace.PublishSurfaceMoves"/>), which matches every
    /// consumer of that producer regardless of which keys they hold — so if any publish could
    /// leak across bundles it would be this one, and it would re-emit the whole of the other
    /// checkout's caller set.
    /// </summary>
    [Fact]
    public void AFullRebuildInAnotherBundle_RebindsNothingInThisOne()
    {
        var root = NewRoot();
        try
        {
            var producerIdentity = Guid.NewGuid();
            var bundleA = Path.Combine(root, "checkout-a");
            var bundleB = Path.Combine(root, "checkout-b");

            var producerInA = RadWorkspaceStore.For(
                "Delta Lib", producerIdentity, Path.Combine(bundleA, "Lib"), bundleA);
            var producerInB = RadWorkspaceStore.For(
                "Delta Lib", producerIdentity, Path.Combine(bundleB, "Lib"), bundleB);

            var consumerInB = RadWorkspaceStore.For(
                "Delta Bridge", Guid.NewGuid(), Path.Combine(bundleB, "Bridge"), bundleB);
            CommitCrossAppEdge(consumerInB, ConsumerCaller, producerInB.Identity, ProducerSurface);

            producerInA.PublishSurfaceMoves(Array.Empty<RadObjectKey>(), fullRebuild: true);
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumerInB));

            producerInB.PublishSurfaceMoves(Array.Empty<RadObjectKey>(), fullRebuild: true);
            var rebind = Assert.Single(RadWorkspaceStore.PendingCrossAppRebinds(consumerInB));
            Assert.Same(producerInB, rebind.Producer);
            Assert.True(rebind.Everything);
            Assert.Equal([ConsumerCaller], rebind.Users);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The watermark is per producer IDENTITY, and both checkouts share one. So consuming this
    /// bundle's producer must not also mark the other bundle's publish as seen: were the two
    /// ever merged, a consumer that legitimately compiled in bundle B would silence a pending
    /// rebind for the identical app in bundle A. Asserted from A's side, where the answer is a
    /// rebind that survives rather than an absence.
    /// </summary>
    [Fact]
    public void ConsumingThisBundlesProducer_LeavesTheOtherBundlesConsumerStillPending()
    {
        var root = NewRoot();
        try
        {
            var producerIdentity = Guid.NewGuid();
            var bundleA = Path.Combine(root, "checkout-a");
            var bundleB = Path.Combine(root, "checkout-b");

            var producerInA = RadWorkspaceStore.For(
                "Delta Lib", producerIdentity, Path.Combine(bundleA, "Lib"), bundleA);
            var producerInB = RadWorkspaceStore.For(
                "Delta Lib", producerIdentity, Path.Combine(bundleB, "Lib"), bundleB);

            var consumerInA = RadWorkspaceStore.For(
                "Delta Bridge", Guid.NewGuid(), Path.Combine(bundleA, "Bridge"), bundleA);
            var consumerInB = RadWorkspaceStore.For(
                "Delta Bridge", Guid.NewGuid(), Path.Combine(bundleB, "Bridge"), bundleB);
            CommitCrossAppEdge(consumerInA, ConsumerCaller, producerInA.Identity, ProducerSurface);
            CommitCrossAppEdge(consumerInB, ConsumerCaller, producerInB.Identity, ProducerSurface);

            producerInA.PublishSurfaceMoves([ProducerSurface], fullRebuild: false);
            producerInB.PublishSurfaceMoves([ProducerSurface], fullRebuild: false);

            // B compiles and records what it consumed.
            RadWorkspaceStore.RecordConsumedGenerations(consumerInB);
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumerInB));

            // A's rebind is untouched by that.
            var rebind = Assert.Single(RadWorkspaceStore.PendingCrossAppRebinds(consumerInA));
            Assert.Same(producerInA, rebind.Producer);
            Assert.Equal([ConsumerCaller], rebind.Users);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "al-runner-rad-bundle-scope", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Give <paramref name="consumer"/> one committed cross-app edge, through the same commit
    /// token a compile produces — a map poked in directly would not prove the query reads what
    /// <c>Commit</c> stores.
    /// </summary>
    private static void CommitCrossAppEdge(
        RadWorkspace consumer, RadObjectKey source, string producerIdentity, RadObjectKey target)
    {
        var file = Path.Combine(consumer.SourceRoot, "src", $"{source.Kind}{source.Id}.al");
        consumer.Commit(new RadWorkspaceUpdate(
            FileHashes: new Dictionary<string, string>(StringComparer.Ordinal) { [file] = "hash" },
            ObjectsByFile: new Dictionary<string, List<RadObjectRef>>(StringComparer.Ordinal)
            {
                [file] = [new RadObjectRef(source, "Caller", string.Empty)],
            },
            DeclarationsByFile: new Dictionary<string, RadFileDeclarations>(StringComparer.Ordinal),
            ReferencesByObject: new Dictionary<RadObjectKey, HashSet<RadObjectKey>>(),
            CrossAppReferencesByObject: new Dictionary<RadObjectKey, HashSet<RadAppObjectRef>>
            {
                [source] = [new RadAppObjectRef(producerIdentity, target)],
            },
            ExtensionTargets: new Dictionary<RadObjectKey, RadObjectKey>(),
            RemovedObjects: Array.Empty<RadObjectKey>(),
            MovedSurfaces: Array.Empty<RadObjectKey>(),
            Baseline: new object(),
            Full: true));
        Assert.Equal([producerIdentity], consumer.CrossAppProducers());
    }
}
