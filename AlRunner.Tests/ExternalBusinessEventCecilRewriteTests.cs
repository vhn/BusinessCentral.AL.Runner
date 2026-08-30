using AlRunner.Infrastructure;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace AlRunner.Tests;

public sealed class ExternalBusinessEventCecilRewriteTests
{
    private static MethodDefinition AddMethod(
        ModuleDefinition module,
        TypeDefinition type,
        string name,
        TypeReference returnType)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public, returnType);
        type.Methods.Add(method);
        method.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        return method;
    }

    private static TypeDefinition AddScopeType(ModuleDefinition module)
    {
        var type = new TypeDefinition(
            "Microsoft.Dynamics.Nav.Runtime",
            "NavExternalBusinessEventMethodScope`1",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(type);
        return type;
    }

    [Fact]
    public void SyncAndAsyncDelivery_CompleteWithoutTouchingScopeLifecycle()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var type = AddScopeType(module);
        var valueTask = module.ImportReference(typeof(ValueTask));
        var sync = AddMethod(module, type, "RunExternalBusinessEvent", module.TypeSystem.Void);
        var async = AddMethod(module, type, "RunExternalBusinessEventAsync", valueTask);
        var lifecycle = AddMethod(module, type, "OnRunExternalEvent", module.TypeSystem.Void);

        var rewritten = NclCecilRewrite.RewriteExternalBusinessEventDelivery(type);

        Assert.Equal(2, rewritten);
        Assert.Collection(sync.Body.Instructions,
            instruction => Assert.Equal(OpCodes.Ret, instruction.OpCode));
        Assert.DoesNotContain(async.Body.Instructions,
            instruction => instruction.OpCode == OpCodes.Call);
        Assert.Contains(async.Body.Instructions, instruction => instruction.OpCode == OpCodes.Initobj);
        Assert.Equal(OpCodes.Ret, async.Body.Instructions[^1].OpCode);
        Assert.Collection(lifecycle.Body.Instructions,
            instruction => Assert.Equal(OpCodes.Ret, instruction.OpCode));
    }

    [Fact]
    public void MissingDeliveryShape_FailsLoudly()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNclMissingAsync", ModuleKind.Dll);
        var type = AddScopeType(module);
        AddMethod(module, type, "RunExternalBusinessEvent", module.TypeSystem.Void);

        var error = Assert.Throws<InvalidOperationException>(
            () => NclCecilRewrite.RewriteExternalBusinessEventDelivery(type));

        Assert.Contains("RunExternalBusinessEvent()", error.Message);
        Assert.Contains("RunExternalBusinessEventAsync()", error.Message);
    }
}
