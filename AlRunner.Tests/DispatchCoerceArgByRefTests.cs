using AlRunner;
using Microsoft.Dynamics.Nav.Runtime;
using Xunit;

namespace AlRunner.Tests;

/// <summary>
/// Pins <see cref="BcRuntime.CoerceArg"/> — the seam that adapts a publisher event-scope
/// field value to the subscriber parameter's CLR type before MethodInfo.Invoke.
///
/// Issue #1816: a PRECOMPILED application DLL stores a publisher-scope event argument
/// ByRef&lt;T&gt;-wrapped whenever the value slot was captured by reference inside the
/// publishing method (Base App table 27 Item.CheckDocuments does this with
/// <c>currentFieldNo</c> — it passes the slot to a <c>var</c> helper before raising
/// OnAfterCheckDocuments). The subscriber's parameter is declared by value (Int32), and
/// MethodInfo.Invoke refuses the ByRef`1[Int32] → Int32 bind with an ArgumentException,
/// killing the whole dispatch before any subscriber runs. Bundle-compiled publishers never
/// hit this because our emit stores the scope field by value — so the shape is only
/// reachable through precompiled DLLs, which is why the defect is pinned here at the seam
/// (the pattern DispatchEventPublisherDeclTypeTests.cs and DispatchObserveAsyncResultTests.cs
/// established): the end-to-end proof needs a real Base App publisher and lives in the
/// corpus (StefanMaron/BusinessCentral.AL.Language.Tests).
///
/// A ByRef&lt;T&gt; is a getter/setter pair over the captured slot, so the faithful
/// by-value observation is the slot's CURRENT value at dispatch time — exactly what a
/// real service tier hands the subscriber.
/// </summary>
public class DispatchCoerceArgByRefTests
{
    [Fact]
    public void ByRefWrappedInt_ByValueIntParameter_UnwrapsValue()
    {
        int slot = 42;
        var wrapped = new ByRef<int>(() => slot, v => slot = v);

        var coerced = BcRuntime.CoerceArg(wrapped, typeof(int));

        Assert.Equal(42, coerced);
    }

    [Fact]
    public void ByRefWrappedInt_ByValueIntParameter_ReadsCurrentSlotValueNotCaptureTimeValue()
    {
        int slot = 1;
        var wrapped = new ByRef<int>(() => slot, v => slot = v);
        slot = 7; // publisher body mutated the slot after the scope captured it

        var coerced = BcRuntime.CoerceArg(wrapped, typeof(int));

        Assert.Equal(7, coerced);
    }

    [Fact]
    public void ByRefWrappedString_ByValueStringParameter_UnwrapsValue()
    {
        string slot = "FIELD CAPTION";
        var wrapped = new ByRef<string>(() => slot, v => slot = v);

        var coerced = BcRuntime.CoerceArg(wrapped, typeof(string));

        Assert.Equal("FIELD CAPTION", coerced);
    }

    [Fact]
    public void ByRefWrappedInt_ByRefParameter_PassesSameInstanceThrough()
    {
        // Subscriber declares the parameter var → emitted as ByRef<int>. The wrapper must
        // pass through IDENTICALLY so subscriber writes still reach the publisher's slot.
        int slot = 42;
        var wrapped = new ByRef<int>(() => slot, v => slot = v);

        var coerced = BcRuntime.CoerceArg(wrapped, typeof(ByRef<int>));

        Assert.Same(wrapped, coerced);
    }

    [Fact]
    public void PlainInt_ByValueIntParameter_PassesThroughUnchanged()
    {
        var coerced = BcRuntime.CoerceArg(42, typeof(int));

        Assert.Equal(42, coerced);
    }
}
