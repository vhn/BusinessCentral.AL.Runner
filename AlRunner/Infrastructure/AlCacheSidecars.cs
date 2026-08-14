// AlCacheSidecars — the completeness rule for an AL-output cache entry.
//
// A cache HIT skips Emit+Compile entirely, so every piece of state that emit
// populated as a SIDE EFFECT is lost unless it was persisted to a sidecar and
// replayed. Two such side effects exist today:
//
//   <key>.enum-registry.json  — AlEnumMetadataRegistry (BcCompiler.CaptureOutputter)
//   <key>.query-symbols.json  — the compilation's SymbolReference, which carries the
//                               BC-compiler-assigned query column ids. RecordPatches
//                               builds a query's MetaQuery design from it; without it
//                               NCLMetaQuery is null and BC throws a
//                               NullReferenceException inside
//                               NavQuery.ValidateTablesNotVirtual on the first Find.
//
// The query sidecar is only written for bundles that actually declare an AL query, so
// it is required for a HIT only when the bundle declares one. That also self-heals
// cache entries written before the sidecar existed: they simply miss once.
using System.Reflection.Metadata;

namespace AlRunner.Infrastructure;

public static class AlCacheSidecars
{
    public const string EnumRegistrySuffix = ".enum-registry.json";
    public const string QuerySymbolsSuffix = ".query-symbols.json";

    // The RAD delta baseline: `--watch`'s per-app compiler symbol picture plus the object /
    // dependency maps a delta binds against (AlRunner.Rad.RadBaselineSidecar). Written only by
    // a watch cycle that actually built a baseline, so a one-shot run's cache entry has none.
    //
    // Deliberately NOT part of IsCompleteEntry, unlike the two above. Those carry side effects
    // a HIT cannot function without — an empty enum registry or a null MetaQuery is a wrong
    // answer, so their absence must force a MISS. These two carry an OPTIMISATION: without
    // them a HIT still serves correct results, it just cannot delta until the first edit has
    // built a baseline. Gating a HIT on them would turn every cache entry written by a
    // one-shot run (all of CI's) into a MISS, and would force a schema bump that discards
    // every existing entry — both to withhold something that is only ever a speedup.
    public const string RadBaselineSuffix = ".rad-baseline.json";
    public const string RadSymbolsSuffix = ".rad-symbols.json";

    /// <summary>
    /// True when a cache entry carries every artifact a HIT needs. A bundle declaring an
    /// AL query additionally requires its query-symbols sidecar.
    /// </summary>
    public static bool IsCompleteEntry(
        bool dllExists, bool enumSidecarExists, bool bundleDeclaresQuery, bool querySidecarExists)
        => dllExists && enumSidecarExists && (!bundleDeclaresQuery || querySidecarExists);

    /// <summary>
    /// Rejects a cache-entry DLL that is present but truncated or otherwise not a loadable
    /// managed assembly image. Defence in depth for issue #1810: even with the write path
    /// publishing atomically (AlCacheWriter — DLL is the last rename, so its presence means
    /// "complete"), a short/corrupt file can still land here from something no write-ordering
    /// discipline prevents — a GH Actions cache restore truncated mid-transfer, a full disk
    /// during the rename's temp-file write, a killed process before the rename. Without this
    /// check, File.ReadAllBytes on such a file is not an I/O error — it just hands back
    /// whatever bytes are on disk — and the truncated bytes flow straight to Assembly.Load,
    /// which throws BadImageFormatException far from the cache-classification code, reading as
    /// mysterious flakiness rather than a cache problem.
    /// Throws <see cref="InvalidDataException"/> (never lets a BadImageFormatException escape
    /// un-wrapped) so callers can catch one exception type and fall through to cache MISS.
    /// </summary>
    public static void ValidateCachedAssemblyBytes(byte[] bytes, string cachePath)
    {
        try
        {
            using var peReader = new System.Reflection.PortableExecutable.PEReader(
                new MemoryStream(bytes, writable: false));
            if (!peReader.HasMetadata)
                throw new BadImageFormatException("no CLI metadata directory — not a managed assembly image");
            _ = peReader.GetMetadataReader();
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or EndOfStreamException
            or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"AL-output cache entry is truncated or corrupt ({bytes.Length} bytes): {ex.Message}", ex);
        }
    }
}
