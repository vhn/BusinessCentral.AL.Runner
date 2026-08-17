using AlRunner.Rad;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// The four properties the cross-app rebind design CLAIMS, each turned into something that
/// fails when the mechanism behind it is taken away.
///
/// <para><see cref="RadDeltaWatchTests"/> already pins the happy path end to end — a moved
/// member id and an added overload both rebind their sibling caller, and a body-only edit does
/// not. What it cannot reach is the shape of the SIGNAL: that it is a per-producer generation
/// counter read against a per-consumer watermark, published at commit rather than at emit. Four
/// distinct bugs live in that shape and none of them is visible on a two-app chain edited one
/// app at a time:</para>
///
/// <list type="number">
/// <item><b>Diamond.</b> Two dependents of one producer. A signal that is TAKEN rather than READ
///   is consumed by whichever asks first, and the second is left dispatching the old ids —
///   silently, since the overload case proves the retired id can survive.</item>
/// <item><b>Failed backend load.</b> A generation whose generated C# Roslyn rejects never loads.
///   Nothing may bind to it, and nothing may record having consumed it, or the rebind is lost
///   for good once the producer's retry succeeds.</item>
/// <item><b>Removal during a full rebuild.</b> A full compile is preceded by
///   <see cref="RadWorkspace.Invalidate"/>, which drops the object map — so no per-key answer
///   about what moved can exist afterwards, and the broadcast has to be "assume everything".
///   A regression that quietly downgraded that to "nothing moved" would leave every consumer of
///   a DELETED object bound to it.</item>
/// <item><b>Consumer before producer.</b> <c>BuildAppGroups</c> orders an app after every sibling
///   it depends on, but falls back to declaration order on a dependency cycle, so a consumer can
///   compile first. The signal must survive to the next cycle rather than be dropped.</item>
/// </list>
///
/// <para><b>Why most of this is not an end-to-end watch test.</b> Every claim above is about the
/// bookkeeping between two workspaces, and it is stated in <see cref="RadWorkspace"/> and
/// <see cref="RadWorkspaceStore"/> in full. Driving four real apps through a real
/// <c>--watch</c> process to observe it would take minutes per assertion, need a dependency
/// CYCLE that BC will not compile at all (guard 4), and still not let the test choose which app
/// commits first. The two guards whose claim also involves the COMPILER — that a rejected C#
/// candidate really does leave the AL emit's surface move unpublished, and that a broadcast
/// really does re-emit the consumer's object — use the real 20-object fixture and the real
/// <c>EmitIncremental</c> path instead.</para>
///
/// <para>The synthetic workspaces below hold hand-built cross-app edges. That is deliberate and
/// it is not the part under test: that a real compile RECORDS such an edge is exactly what
/// <see cref="RadDeltaWatchTests"/> proves, and repeating it here would need sibling-symbol
/// plumbing to compile a second app in-process. What these tests own is what happens to the
/// edge once it exists.</para>
///
/// <para><b>Each guard was falsified before it was committed</b>, by disabling the mechanism it
/// names and confirming it fails at its own load-bearing assertion:</para>
///
/// <list type="table">
/// <item><term>1 diamond</term><description><c>MovesSince</c> made to REMOVE the keys it returns
///   (a drained queue) — the second dependent's rebind comes back empty.</description></item>
/// <item><term>2 failed load</term><description><c>PublishSurfaceMoves</c> called from
///   <c>DeltaCompile</c> instead of <c>RadEmitResult.Commit</c> — the producer's generation
///   advances to 2 for a candidate the C# backend rejected. Its second half falls to advancing
///   the watermark inside <c>PendingCrossAppRebinds</c> instead of at the commit: the rebind
///   disappears after being read once.</description></item>
/// <item><term>3 full rebuild</term><description><c>PublishSurfaceMoves</c> returning early on
///   <c>fullRebuild</c> — the consumer of the deleted object has nothing
///   pending.</description></item>
/// <item><term>4 ordering</term><description>the widening moved BELOW the no-change
///   short-circuit — the consumer takes NoChange forever and never re-emits. Also falls to
///   <c>RecordConsumed</c> storing a "caught up" sentinel instead of the producer's actual
///   generation.</description></item>
/// </list>
///
/// <para>Those mechanisms are not four independent things — 1, 2 and 4 all rest on the same
/// invariant (durable per-producer generations, read against a per-consumer watermark that only
/// a commit advances), so a single disabling can break more than one guard. What is distinct is
/// the SCENARIO each one covers, and each ships a different wrong answer.</para>
/// </summary>
[Collection(BcEngineCollection.Name)]
public sealed class RadCrossAppRebindGuardTests(BcEngineFixture engine)
{
    private const string ScenarioDir = "al-runner-rad-xapp-guard";

    // ── guard 1: the diamond ──────────────────────────────────────────────────────────

    /// <summary>
    /// One producer, TWO dependents, in one cycle: both rebind, and an app that references
    /// neither does not.
    ///
    /// <para>This is the whole reason the signal is a watermark that is READ rather than a queue
    /// that is DRAINED. A drained signal is consumed by whichever dependent asks first — and the
    /// order they ask in is `BuildAppGroups`' topological order, which says nothing about
    /// siblings that do not depend on each other. The second dependent would then take the
    /// NoChange short-circuit and keep executing IL that bakes the producer's previous member
    /// ids, with no diagnostic anywhere: adding an overload leaves the retired id resolvable, so
    /// the stale caller gets an ordinary answer that happens to be the previous one.</para>
    ///
    /// <para>The bystander is the negative half. "Rebind everything in the bundle" would satisfy
    /// the first claim and destroy the point of the delta path, so an app whose only cross-app
    /// edge is into a DIFFERENT producer must be left alone — and so must both dependents once
    /// they have consumed, which is what the last two assertions pin.</para>
    /// </summary>
    [Fact]
    public void ASurfaceMoveRebindsBothDependentsOfADiamond_AndLeavesTheBystanderAlone()
    {
        var bundle = NewBundleRoot();
        var producer = App(bundle, "Guard Diamond Producer");
        var other = App(bundle, "Guard Diamond Other");
        var left = App(bundle, "Guard Diamond Left");
        var right = App(bundle, "Guard Diamond Right");
        var bystander = App(bundle, "Guard Diamond Bystander");

        var moved = Key(50100);
        var untouched = Key(50101);
        var elsewhere = Key(50200);
        var leftCaller = Key(50300);
        var rightCaller = Key(50400);
        var bystanderCaller = Key(50500);

        // ── cycle 1: everything compiles cold, so every app broadcasts a full rebuild ──
        ColdCycle(producer, [moved, untouched]);
        ColdCycle(other, [elsewhere]);
        ColdCycle(left, [leftCaller], (leftCaller, producer.Identity, moved));
        ColdCycle(right, [rightCaller], (rightCaller, producer.Identity, moved));
        ColdCycle(bystander, [bystanderCaller], (bystanderCaller, other.Identity, elsewhere));
        foreach (var consumer in new[] { left, right, bystander })
        {
            RadWorkspaceStore.RecordConsumedGenerations(consumer);
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer));
        }
        Assert.Equal(1, producer.PublishGeneration);
        Assert.Equal(1, left.WatermarkFor(producer.Identity));
        Assert.Equal(1, right.WatermarkFor(producer.Identity));

        // ── cycle 2: the producer moves ONE surface ──
        producer.PublishSurfaceMoves([moved], fullRebuild: false);
        Assert.Equal(2, producer.PublishGeneration);

        // Left compiles first — and commits, which is where a drain would happen.
        var forLeft = Assert.Single(RadWorkspaceStore.PendingCrossAppRebinds(left));
        Assert.Same(producer, forLeft.Producer);
        Assert.False(forLeft.Everything);
        Assert.Equal([leftCaller], forLeft.Users);
        RadWorkspaceStore.RecordConsumedGenerations(left);

        // Right compiles second, in the SAME cycle. The signal was read, not taken.
        var forRight = Assert.Single(RadWorkspaceStore.PendingCrossAppRebinds(right));
        Assert.Same(producer, forRight.Producer);
        Assert.False(forRight.Everything);
        Assert.Equal([rightCaller], forRight.Users);
        RadWorkspaceStore.RecordConsumedGenerations(right);

        // The bystander calls a different app entirely and is not widened by any of this.
        Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(bystander));
        RadWorkspaceStore.RecordConsumedGenerations(bystander);

        // ── cycle 3: nothing moved. Both dependents are bound to generation 2 and stay warm —
        // a rebind that fired again here would be "rebind every consumer, every cycle".
        Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(left));
        Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(right));
        Assert.Equal(2, left.WatermarkFor(producer.Identity));
        Assert.Equal(2, right.WatermarkFor(producer.Identity));

        // ── cycle 4: a body-only edit in the producer publishes NOTHING at all, so the
        // generation does not even advance and neither dependent is asked to do anything.
        producer.PublishSurfaceMoves([], fullRebuild: false);
        Assert.Equal(2, producer.PublishGeneration);
        Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(left));
        Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(right));
    }

    // ── guard 2: a generation the C# backend rejected ─────────────────────────────────

    /// <summary>
    /// A producer whose generated C# Roslyn refuses announces nothing, and the rebind it owes
    /// its consumers survives to the cycle that finally loads.
    ///
    /// <para>The AL emit succeeds here — `Database.ExportData` is valid AL whose generated C# is
    /// not, the same shape <see cref="RadDeltaWatchTests"/> uses — so the delta really does
    /// compute a moved surface and really does hand back a committable workspace update. What
    /// must not happen is that anyone acts on it: the assembly never loads, so a dependent that
    /// rebound against it would be binding to member ids no running code has.</para>
    ///
    /// <para><b>The load-bearing half is the second one.</b> Publishing at AL-emit time would not
    /// merely be premature — the dependent would also RECORD having consumed that generation, and
    /// when the producer's retry finally loads, its watermark would already cover it. The rebind
    /// would be dropped once and never come back, which is the original bug restored by its own
    /// fix. So the test asserts that the pending rebind is still there after being read (a
    /// consumer whose own C# is rejected does not commit either), and that only
    /// <c>RecordConsumedGenerations</c> — called from <c>RadEmitResult.Commit</c> — retires
    /// it.</para>
    /// </summary>
    [SkippableFact]
    public void AProducerWhoseBackendRejectsItsCSharp_PublishesNothing_AndTheNextCycleStillRebinds()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var bundle = NewBundleRoot();
        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var producer = RadFixture.Seed(tempRoot, bundleRoot: bundle);
            var callee = Key(71005);   // "RAD Perf Unrelated D", which nothing in its own app calls
            var consumer = App(bundle, "Guard Rejected Consumer");
            var caller = Key(50300);
            ColdCycle(consumer, [caller], (caller, producer.Workspace.Identity, callee));
            RadWorkspaceStore.RecordConsumedGenerations(consumer);
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer));
            Assert.Equal(1, producer.Workspace.PublishGeneration);

            // Move the callee's surface (a new member) AND make its generated C# unacceptable.
            File.WriteAllText(
                RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"),
                """
                namespace AlRunner.Tests.RadTwentyObject;

                codeunit 71005 "RAD Perf Unrelated D"
                {
                    procedure Value(): Integer
                    var
                        FileName: Text;
                    begin
                        Database.ExportData(false, FileName);
                        exit(105);
                    end;

                    procedure Extra(): Integer
                    begin
                        exit(1);
                    end;
                }
                """);

            var rejected = producer.Cycle(tempRoot);
            Assert.False(rejected.FullRebuild);
            Assert.True(rejected.Emit.Diagnostics.Count == 0,
                string.Join(Environment.NewLine, rejected.Emit.Diagnostics));
            Assert.Equal(["RAD Perf Unrelated D"], RadFixture.EmittedNames(rejected));
            // The AL emit DID see the surface move — this is a live signal being withheld, not
            // an empty one that would prove nothing.
            Assert.Contains(callee, rejected.WorkspaceUpdate!.MovedSurfaces);

            var compiled = RadFixture.TryAssemble(producer.Workspace, rejected.Emit.Sources);
            Assert.False(compiled.Success,
                "the fixture edit no longer produces C# Roslyn rejects; pick another");

            // Not committed, so not published: no dependent may bind to a generation that
            // never loaded.
            Assert.Equal(1, producer.Workspace.PublishGeneration);
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer));
            // …and the consumer's own cycle runs and commits regardless. It must not record
            // having consumed something that was never announced.
            RadWorkspaceStore.RecordConsumedGenerations(consumer);
            Assert.Equal(1, consumer.WatermarkFor(producer.Workspace.Identity));

            // Repair the generated C# while KEEPING the surface move, and let it load.
            File.WriteAllText(
                RadFixture.SourceFile(tempRoot, "RadPerfUnrelatedD.Codeunit.al"),
                """
                namespace AlRunner.Tests.RadTwentyObject;

                codeunit 71005 "RAD Perf Unrelated D"
                {
                    procedure Value(): Integer
                    begin
                        exit(105);
                    end;

                    procedure Extra(): Integer
                    begin
                        exit(1);
                    end;
                }
                """);
            var repaired = producer.Cycle(tempRoot);
            Assert.Equal(["RAD Perf Unrelated D"], RadFixture.EmittedNames(repaired));
            Assert.Contains(callee, repaired.WorkspaceUpdate!.MovedSurfaces);
            repaired.Commit(
                producer.Workspace,
                RadFixture.AssembleAndLoad(producer.Workspace, repaired.Emit.Sources));
            Assert.Equal(2, producer.Workspace.PublishGeneration);

            var pending = Assert.Single(RadWorkspaceStore.PendingCrossAppRebinds(consumer));
            Assert.Same(producer.Workspace, pending.Producer);
            Assert.False(pending.Everything);
            Assert.Equal([caller], pending.Users);

            // Reading the widening does not retire it: a consumer whose own generated C# is
            // rejected never reaches its commit, and the rebind has to be waiting next cycle.
            var again = Assert.Single(RadWorkspaceStore.PendingCrossAppRebinds(consumer));
            Assert.Equal([caller], again.Users);
            Assert.Equal(1, consumer.WatermarkFor(producer.Workspace.Identity));

            // Only the commit retires it.
            RadWorkspaceStore.RecordConsumedGenerations(consumer);
            Assert.Equal(2, consumer.WatermarkFor(producer.Workspace.Identity));
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── guard 3: an object removed by a full rebuild ──────────────────────────────────

    /// <summary>
    /// An object that a full rebuild DELETED still rebinds the consumers that called it, and the
    /// log says which rule did it.
    ///
    /// <para>A full compile is preceded by <see cref="RadWorkspace.Invalidate"/>, which drops the
    /// object map. By the time the new module exists there is no record of what the previous one
    /// declared, so no per-key answer about what moved can be reconstructed — the test asserts
    /// that directly (<c>FileOf</c> returns null for the removed key straight after the
    /// invalidate). <c>PublishSurfaceMoves(fullRebuild: true)</c> therefore broadcasts "assume
    /// everything moved" instead, and this is the case that needs it most: the consumer's edge
    /// points at a key that no longer exists ANYWHERE, so a per-key rule could never match it,
    /// and the consumer would be left dispatching a member id belonging to a deleted object.</para>
    ///
    /// <para>The reason is asserted, not just the fact. <c>which rebuilt in full</c> is what
    /// separates the broadcast from the per-key rule, and cycle 8 of
    /// <see cref="RadDeltaWatchTests"/> asserts its ABSENCE for a one-object delta — the two
    /// together are what stop the broadcast from quietly becoming the answer to everything.</para>
    ///
    /// <para>Compiled for real, because the claim is that the widening is ACTED on: the consumer
    /// is the 20-object fixture with a real baseline, and the cycle has to re-emit exactly the
    /// one object holding the edge, out of twenty.</para>
    /// </summary>
    [SkippableFact]
    public void AnObjectRemovedByAFullRebuild_StillRebindsItsCrossAppConsumers()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var bundle = NewBundleRoot();
        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var consumer = RadFixture.Seed(tempRoot, bundleRoot: bundle);
            var producer = App(bundle, "Guard Rebuild Producer");
            var vanishes = Key(50100);
            var survives = Key(50101);
            var caller = Key(71001);   // "RAD Perf Caller"

            ColdCycle(producer, [vanishes, survives]);
            AddCrossAppEdge(consumer.Workspace, caller, producer.Identity, vanishes);
            RadWorkspaceStore.RecordConsumedGenerations(consumer.Workspace);
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer.Workspace));
            consumer.AssertSettled(tempRoot);

            // The producer deletes the object and rebuilds in full.
            producer.Invalidate("the guard forces a whole-module rebuild");
            Assert.Null(producer.FileOf(vanishes));
            Assert.Null(producer.FileOf(survives));
            ColdCycle(producer, [survives]);
            Assert.False(producer.Declares(vanishes));

            // The consumer has no per-key answer to act on — only the broadcast.
            var pending = Assert.Single(
                RadWorkspaceStore.PendingCrossAppRebinds(consumer.Workspace));
            Assert.Same(producer, pending.Producer);
            Assert.True(pending.Everything);
            Assert.Equal([caller], pending.Users);

            var (cycle, log) = CycleCapturingStderr(consumer, tempRoot);

            Assert.Contains(
                $"{RadFixture.ModuleName}: rebinding 1 cross-app caller file(s) — "
                + "1 that call Guard Rebuild Producer, which rebuilt in full",
                log);
            // The widening is a delta, not a bail-out: a consumer that took the whole module
            // would also "rebind", and would prove nothing about proportionality.
            Assert.DoesNotContain($"{RadFixture.ModuleName}: full compile", log);
            Assert.False(cycle.FullRebuild);
            Assert.False(cycle.NoChange);
            Assert.Equal(["RAD Perf Caller"], RadFixture.EmittedNames(cycle));

            var overlay = RadFixture.AssembleAndLoad(consumer.Workspace, cycle.Emit.Sources);
            cycle.Commit(consumer.Workspace, overlay);
            consumer.AssertOwnership(overlay, ["Codeunit71001"]);
            // Consumed exactly once: the broadcast does not re-fire every cycle.
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer.Workspace));
            consumer.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── guard 4: a consumer that compiled before its producer ─────────────────────────

    /// <summary>
    /// A consumer that compiles BEFORE its producer in the same cycle picks the signal up on the
    /// NEXT cycle instead of losing it.
    ///
    /// <para><c>BuildAppGroups</c> orders an app after every sibling it depends on — but on a
    /// dependency CYCLE it gives up and emits the rest in declaration order (Program.cs: "If none
    /// qualify the graph has a cycle — emit the rest in declaration order rather than looping
    /// forever"). A consumer can therefore run before the producer whose surface moves later in
    /// the same pass, and there is nothing the ordering can do about it.</para>
    ///
    /// <para><b>The rebind is one cycle late, and that is the designed behaviour — not a defect
    /// to be fixed by re-running the bundle loop.</b> The consumer commits with the producer's
    /// generation AS IT WAS when it compiled; the producer then publishes a higher one; the
    /// comparison is <c>&gt;</c>, so the next cycle sees it. A design that instead marked a
    /// consumer "caught up" at its own commit — the intuitive reading of "this app has just been
    /// rebuilt" — would swallow the publish that had not happened yet, permanently. The
    /// alternative fix, re-entering the bundle loop until no publish is outstanding, would turn
    /// an already-degenerate case (a dependency cycle BC itself reports as unresolved) into an
    /// unbounded one.</para>
    ///
    /// <para>Compiled for real, and the middle cycle is the reason. The consumer's own source
    /// never moves in this test, so the ONLY thing that can make it compile is the widening —
    /// and that widening is computed before the no-change short-circuit precisely because the
    /// consumer's file hashes are what did not change. The cycle where nothing is pending must
    /// therefore report <c>NoChange</c>, and the cycle after the producer's late publish must
    /// re-emit, from the identical tree. A regression that moved the widening after the
    /// short-circuit passes the first and fails the second.</para>
    ///
    /// <para>Deliberately not an end-to-end watch test: the tree it would need is a genuine
    /// app.json dependency cycle, which is exactly the input the runner cannot compile, and even
    /// then the test could not choose which app commits first.</para>
    /// </summary>
    [SkippableFact]
    public void AConsumerThatCompiledBeforeItsProducer_RebindsOnTheNextCycle()
    {
        TestArtifacts.SkipIf(!engine.Ready, engine.SkipReason ?? "BC engine not ready");

        var bundle = NewBundleRoot();
        var tempRoot = RadFixture.Copy(ScenarioDir);
        try
        {
            using var identity = BcCompiler.ScopeCurrentAppIdentity(
                RadFixture.AppId, RadFixture.Publisher, RadFixture.AppVersion);
            var producer = App(bundle, "Guard Order Producer");
            var moved = Key(50100);
            var caller = Key(71001);   // "RAD Perf Caller"

            // ── cycle 1: both cold ──
            ColdCycle(producer, [moved]);
            var consumer = RadFixture.Seed(tempRoot, bundleRoot: bundle);
            AddCrossAppEdge(consumer.Workspace, caller, producer.Identity, moved);
            Assert.Equal(1, producer.PublishGeneration);
            Assert.Equal(1, consumer.Workspace.WatermarkFor(producer.Identity));
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer.Workspace));

            // ── cycle 2: the CONSUMER compiles first. Nothing is pending, and its own source
            // did not move, so it does no work at all — the widening is asked for before the
            // short-circuit and legitimately answers "nothing".
            var quiet = consumer.Cycle(tempRoot);
            Assert.True(quiet.NoChange);
            Assert.Empty(quiet.Emit.Sources);

            // …and only THEN does the producer move a surface and commit, later in the same
            // cycle. Nothing will ask the consumer again until the next one.
            producer.PublishSurfaceMoves([moved], fullRebuild: false);
            Assert.Equal(2, producer.PublishGeneration);
            // A NoChange cycle commits nothing, so the consumer is still at generation 1 and
            // the publish it never saw is still ahead of it.
            Assert.Equal(1, consumer.Workspace.WatermarkFor(producer.Identity));

            var pending = Assert.Single(
                RadWorkspaceStore.PendingCrossAppRebinds(consumer.Workspace));
            Assert.Same(producer, pending.Producer);
            Assert.False(pending.Everything);
            Assert.Equal([caller], pending.Users);

            // ── cycle 3: one cycle late, by design — but not lost ──
            var (cycle, log) = CycleCapturingStderr(consumer, tempRoot);

            Assert.Contains(
                $"{RadFixture.ModuleName}: rebinding 1 cross-app caller file(s) — "
                + "1 that call Guard Order Producer",
                log);
            // Per-key, not the full-rebuild broadcast: the producer named exactly what moved.
            Assert.DoesNotContain("which rebuilt in full", log);
            Assert.DoesNotContain($"{RadFixture.ModuleName}: full compile", log);
            Assert.False(cycle.FullRebuild);
            Assert.False(cycle.NoChange);
            Assert.Equal(["RAD Perf Caller"], RadFixture.EmittedNames(cycle));

            var overlay = RadFixture.AssembleAndLoad(consumer.Workspace, cycle.Emit.Sources);
            cycle.Commit(consumer.Workspace, overlay);
            consumer.AssertOwnership(overlay, ["Codeunit71001"]);

            // ── cycle 4: and exactly once ──
            Assert.Equal(2, consumer.Workspace.WatermarkFor(producer.Identity));
            Assert.Empty(RadWorkspaceStore.PendingCrossAppRebinds(consumer.Workspace));
            consumer.AssertSettled(tempRoot);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    // ── harness ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A bundle root nothing else in the process shares. <see cref="RadWorkspaceStore"/>'s map is
    /// static and never cleared, so every one of these tests would otherwise see the workspaces
    /// of the ones that ran before it. The path is never created: nothing on these paths is read
    /// from disk.
    /// </summary>
    private static string NewBundleRoot() => Path.Combine(
        Path.GetTempPath(), ScenarioDir, "bundle-" + Guid.NewGuid().ToString("N"));

    private static RadWorkspace App(string bundleRoot, string moduleName) =>
        RadWorkspaceStore.For(
            moduleName, Guid.NewGuid(), Path.Combine(bundleRoot, moduleName), bundleRoot);

    private static RadObjectKey Key(int id) => new("Codeunit", id);

    /// <summary>
    /// One warm cycle with stderr captured, because the rebind's REASON is only ever written
    /// there — <c>RadCycleNotes</c> collects full-compile decisions, not widenings.
    ///
    /// <para>The empty-capture assertion is not defensive noise. Every RAD cycle logs at least
    /// one <c>[watch]</c> line, so an empty buffer cannot mean "nothing happened": it means
    /// another collection swapped <see cref="Console.Error"/> inside this window. Collections run
    /// four wide (<c>xunit.runner.json</c>) and three other suites capture stderr the same way,
    /// so that is possible, and without this assertion it surfaces as "substring not found in
    /// the empty string" — indistinguishable from the regression the test exists to catch.</para>
    /// </summary>
    private static (RadEmitResult Result, string Log) CycleCapturingStderr(
        SeededBaseline baseline, string tempRoot)
    {
        var savedErr = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        RadEmitResult result;
        try { result = baseline.Cycle(tempRoot); }
        finally { Console.SetError(savedErr); }
        var log = captured.ToString();
        Assert.False(
            log.Length == 0,
            "stderr came back empty, which no RAD cycle produces — another test collection "
            + "swapped Console.Error during this window. Re-run in isolation before reading "
            + "this as a regression.");
        return (result, log);
    }

    /// <summary>
    /// One app's first cycle: a whole-module compile committed, then the broadcast its
    /// <c>RadEmitResult.Commit</c> makes — which for a full rebuild is always "assume everything
    /// moved", because the object map it would have to diff against was cleared before it ran.
    /// </summary>
    private static void ColdCycle(
        RadWorkspace ws,
        RadObjectKey[] declares,
        params (RadObjectKey Source, string Producer, RadObjectKey Target)[] crossApp)
    {
        var file = Path.Combine(ws.SourceRoot, "src", "Objects.al");
        var objectsByFile = new Dictionary<string, List<RadObjectRef>>(StringComparer.Ordinal)
        {
            [file] = declares
                .Select(key => new RadObjectRef(key, $"{ws.ModuleName} {key.Id}", string.Empty))
                .ToList(),
        };
        var edges = new Dictionary<RadObjectKey, HashSet<RadAppObjectRef>>();
        foreach (var (source, producer, target) in crossApp)
        {
            if (!edges.TryGetValue(source, out var targets))
                edges[source] = targets = new HashSet<RadAppObjectRef>();
            targets.Add(new RadAppObjectRef(producer, target));
        }
        ws.Commit(new RadWorkspaceUpdate(
            FileHashes: new Dictionary<string, string>(StringComparer.Ordinal) { [file] = "seed" },
            ObjectsByFile: objectsByFile,
            DeclarationsByFile: new Dictionary<string, RadFileDeclarations>(StringComparer.Ordinal),
            ReferencesByObject: new Dictionary<RadObjectKey, HashSet<RadObjectKey>>(),
            CrossAppReferencesByObject: edges,
            ExtensionTargets: new Dictionary<RadObjectKey, RadObjectKey>(),
            RemovedObjects: Array.Empty<RadObjectKey>(),
            MovedSurfaces: Array.Empty<RadObjectKey>(),
            Baseline: new object(),
            Full: true));
        ws.PublishSurfaceMoves(Array.Empty<RadObjectKey>(), fullRebuild: true);
    }

    /// <summary>
    /// Give a REAL compiled workspace the one cross-app edge these tests are about, by
    /// re-committing its own snapshot with that edge added. Everything else — the object map,
    /// the file hashes, the symbol baseline — is exactly what the compile produced, so the cycle
    /// that follows is a real delta over a real baseline.
    /// </summary>
    private static void AddCrossAppEdge(
        RadWorkspace ws, RadObjectKey source, string producer, RadObjectKey target)
    {
        var snapshot = ws.Snapshot();
        Assert.NotNull(snapshot);
        Assert.NotNull(ws.FileOf(source));
        var edges = snapshot.CrossAppReferencesByObject
            .ToDictionary(pair => pair.Key, pair => new HashSet<RadAppObjectRef>(pair.Value));
        edges[source] = [new RadAppObjectRef(producer, target)];
        ws.Commit(snapshot with { CrossAppReferencesByObject = edges });
    }
}
