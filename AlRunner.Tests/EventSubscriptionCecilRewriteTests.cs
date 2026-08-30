using AlRunner.Infrastructure;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace AlRunner.Tests;

public sealed class EventSubscriptionCecilRewriteTests
{
    [Fact]
    public void RuntimeBackedSubscriptionChecks_ArePreserved()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var handlerType = new TypeDefinition(
            "Microsoft.Dynamics.Nav.EventSubscription",
            "NavTriggerEventHandler",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(handlerType);
        var handlerCheck = new MethodDefinition(
            "IsEventSubscribed",
            MethodAttributes.Public,
            module.TypeSystem.Boolean);
        handlerCheck.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        handlerType.Methods.Add(handlerCheck);

        var metadataType = new TypeDefinition(
            "Microsoft.Dynamics.Nav.Runtime",
            "NCLMetaApplicationObject",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(metadataType);
        var metadataCheck = new MethodDefinition(
            "IsEventSubscribed",
            MethodAttributes.Public,
            module.TypeSystem.Boolean);
        metadataCheck.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        metadataType.Methods.Add(metadataCheck);
        var il = metadataCheck.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Callvirt, handlerCheck));
        il.Append(il.Create(OpCodes.Ret));

        var originalInstructions = metadataCheck.Body.Instructions.ToArray();

        var preserved = NclCecilRewrite.PreserveRuntimeEventSubscriptionChecks(metadataType);

        Assert.Equal(1, preserved);
        Assert.Equal(originalInstructions, metadataCheck.Body.Instructions);
        Assert.Contains(metadataCheck.Body.Instructions, instruction =>
            instruction.Operand is MethodReference method
            && method.DeclaringType.FullName == handlerType.FullName
            && method.Name == "IsEventSubscribed");
    }
}
