// CompanyInitializer — make the runner's company look like a company that exists.
//
// WHY
//   Install triggers are not what creates a BC company's baseline rows. In real BC that is
//   company CREATION, which runs codeunit 2 "Company-Initialize" — and that is what inserts
//   the Company Information record, the Source Code Setup, the setup No. Series and so on.
//   The runner fired install triggers and nothing else, so those rows simply did not exist.
//
//   That went unnoticed for as long as a failed Record.Get silently succeeded: the caller got
//   a blank record and carried on. Once Get was made to raise (as AL requires when the return
//   value is not consumed), the missing rows surfaced — as a swallowed error five layers up,
//   reported as "report 1306 requires filter information".
//
//   Running BC's own codeunit 2 is the faithful answer rather than fabricating rows here: the
//   rows are then exactly the ones Base App creates. It runs once per bundle, immediately
//   before the install baseline is captured, so its rows are part of the committed baseline
//   every test is restored to.
using System.Reflection;

namespace AlRunner;

internal static class CompanyInitializer
{
    // Codeunit 2 "Company-Initialize" (Base App). Absent in bundles that do not carry Base
    // App, which is not an error: those have no company-setup concept to initialize.
    private const int CompanyInitializeCodeunitId = 2;

    private static bool _ranForThisBundle;

    internal static void ResetForNewBundle() => _ranForThisBundle = false;

    /// <summary>
    /// Run BC's own company initialization once per bundle. Call before dependency install
    /// triggers and before CaptureInstallBaseline so the rows it creates are part of the
    /// restored baseline.
    /// </summary>
    internal static void EnsureCompanyInitialized()
    {
        if (_ranForThisBundle) return;
        _ranForThisBundle = true;

        if (BcRuntime.FindCodeunitTypePublic(CompanyInitializeCodeunitId) == null)
            return; // no Base App in this bundle — nothing to initialize.

        // Codeunit 2 runs at the ROOT, outside any test method, and the statement form is only
        // "not a transaction boundary" INSIDE one: BC's plain BeginTransaction bumps
        // TransactionCount when a transaction is already active, but at the root none is, so it
        // commits and pushes a fresh one and EndTransaction(false) rolls it back. Codeunit.Run's
        // own bracket is now guarded-only (CodeunitPatches.RunCodeunitInTransaction), so bracket
        // this call explicitly — otherwise a partially-failing Company-Initialize leaves its rows
        // standing and CaptureInstallBaseline persists them under the dependency cache key.
        // NB this reuses the guarded-run helper for a STATEMENT-FORM root call. Both need the
        // same thing here — mark a commit point, push a frame, restore it on failure — but if
        // BC's ThrowIfWriteTransactionStarted refusal is ever ported into
        // BeginCodeunitRunTransaction (see its doc comment), this call site must NOT inherit it:
        // give the root its own entry point first.
        bool initialized = false;
        Patches.ALDatabasePatches.BeginCodeunitRunTransaction();
        try
        {
            BcRuntime.NavCodeunit_RunCodeunit(
                Microsoft.Dynamics.Nav.Types.DataError.ThrowError, CompanyInitializeCodeunitId, null);
            initialized = true;
            PerfTrace.Log("CompanyInitializer: ran codeunit 2 Company-Initialize");
        }
        catch (Exception ex)
        {
            // Keep initialization failures visible without rejecting bundles that do not use
            // the setup rows codeunit 2 had not reached yet.
            var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
            Console.Error.WriteLine(
                $"[CompanyInitializer] codeunit 2 \"Company-Initialize\" did not complete: " +
                $"{inner.GetType().Name}: {inner.Message} — the partial writes were rolled back, so the " +
                $"company is NOT initialized and AL that reads any setup table will fail. This " +
                $"result is cached under the dependency key: later processes reuse it silently.");
        }
        finally
        {
            Patches.ALDatabasePatches.EndCodeunitRunTransaction(initialized);
        }
    }
}
