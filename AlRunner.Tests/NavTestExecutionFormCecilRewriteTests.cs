using System.Reflection;
using AlRunner.Infrastructure;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace AlRunner.Tests;

public sealed class NavTestExecutionFormCecilRewriteTests
{
    [Fact]
    public void TestHandleForm_RewritesClientCallbackAndPreservesUnhandledOos()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var method = BuildTestHandleForm(module);

        NclCecilRewrite.RewriteTestHandleFormClientCallback(module, method.DeclaringType);

        var calls = method.Body.Instructions
            .Where(instruction => instruction.Operand is MethodReference)
            .Select(instruction => (MethodReference)instruction.Operand)
            .ToArray();
        Assert.DoesNotContain(calls, call => call.Name is
            "get_ServiceConnection" or "get_CallbackHandler" or "Proxy");
        Assert.Contains(calls, call =>
            call.DeclaringType.FullName == "AlRunner.Patches.RunnerModalDispatch"
            && call.Name == "FormRun");
        Assert.Contains(calls, call =>
            call.DeclaringType.FullName == "AlRunner.Patches.RunnerModalDispatch"
            && call.Name == "ThrowUnhandledNonModalForm");

        var findHandler = method.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference call && call.Name == "FindHandler");
        Assert.Equal(OpCodes.Ldc_I4_0, findHandler.Previous!.Previous!.OpCode);

        using var image = new MemoryStream();
        module.Write(image);
        image.Position = 0;
        using var roundTripped = ModuleDefinition.ReadModule(image);
        Assert.NotNull(roundTripped.GetType(method.DeclaringType.FullName));
    }

    [Fact]
    public void TestHandleForm_WithNonVoidClientCallback_FailsLoudly()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var method = BuildTestHandleForm(module, callbackReturnsVoid: false);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleFormClientCallback(module, method.DeclaringType));

        Assert.Contains("FormRun callback signature", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [Fact]
    public void TestHandleForm_WithMissingReceiverChain_FailsLoudly()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var method = BuildTestHandleForm(module);
        var proxy = method.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference call && call.Name == "Proxy");
        method.Body.GetILProcessor().Remove(proxy);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleFormClientCallback(module, method.DeclaringType));

        Assert.Contains("receiver chain", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [Fact]
    public void TestHandleForm_WithChangedNullHandlerBranch_FailsLoudly()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var method = BuildTestHandleForm(module);
        var findHandler = method.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference call && call.Name == "FindHandler");
        method.Body.GetILProcessor().Remove(findHandler.Next!);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleFormClientCallback(module, method.DeclaringType));

        Assert.Contains("null-handler branch", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [Fact]
    public void TestHandleForm_WithAmbiguousTarget_FailsWithShapeDiagnostic()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var method = BuildTestHandleForm(module);
        var duplicate = new MethodDefinition(
            method.Name,
            MethodAttributes.Public,
            module.TypeSystem.Boolean);
        duplicate.Parameters.Add(new ParameterDefinition(method.Parameters[0].ParameterType));
        duplicate.Parameters.Add(new ParameterDefinition(method.Parameters[1].ParameterType));
        duplicate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldc_I4_0));
        duplicate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        method.DeclaringType.Methods.Add(duplicate);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleFormClientCallback(module, method.DeclaringType));

        Assert.Contains("found 2", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    private static MethodDefinition BuildTestHandleForm(
        ModuleDefinition module,
        bool callbackReturnsVoid = true)
    {
        var testExecution = new TypeDefinition(
            "Microsoft.Dynamics.Nav.Runtime",
            "NavTestExecution",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(testExecution);

        var navForm = AddType(module, "Microsoft.Dynamics.Nav.Runtime", "NavForm");
        var runtimeParameters = AddType(
            module, "Microsoft.Dynamics.Nav.Runtime", "NavFormRuntimeParameters");
        var runRequest = AddType(module, "Microsoft.Dynamics.Nav.Types", "FormRunRequest");
        var service = AddType(module, "Microsoft.Dynamics.Nav.Client", "IService");
        var callback = AddType(module, "Microsoft.Dynamics.Nav.Types", "IClientCallbackHandler");
        var proxy = AddType(module, "Microsoft.Dynamics.Nav.Types", "TestClientProxy`1");

        var getService = AddMethod(testExecution, "get_ServiceConnection", service);
        var getCallback = AddMethod(service, "get_CallbackHandler", callback);
        var proxyMethod = AddMethod(proxy, "Proxy", callback, callback);
        proxyMethod.IsStatic = true;
        var formRun = AddMethod(
            callback,
            "FormRun",
            callbackReturnsVoid ? module.TypeSystem.Void : module.TypeSystem.Int32,
            runRequest);

        var findHandler = AddMethod(
            testExecution,
            "FindHandler",
            module.ImportReference(typeof(MethodInfo)),
            module.TypeSystem.Int32,
            module.TypeSystem.Object,
            module.TypeSystem.Boolean,
            module.TypeSystem.String);

        var method = new MethodDefinition(
            "TestHandleForm",
            MethodAttributes.Public,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition("form", ParameterAttributes.None, navForm));
        method.Parameters.Add(new ParameterDefinition("parameters", ParameterAttributes.None, runtimeParameters));
        testExecution.Methods.Add(method);

        var handler = new VariableDefinition(module.ImportReference(typeof(MethodInfo)));
        var request = new VariableDefinition(runRequest);
        method.Body.Variables.Add(handler);
        method.Body.Variables.Add(request);
        method.Body.InitLocals = true;

        var equality = module.ImportReference(typeof(MethodInfo).GetMethod(
            "op_Equality",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(MethodInfo), typeof(MethodInfo)],
            modifiers: null)!);
        var il = method.Body.GetILProcessor();
        var handlerFound = Instruction.Create(OpCodes.Ldarg_0);
        var loadExecution = Instruction.Create(OpCodes.Ldarg_0);

        il.Append(Instruction.Create(OpCodes.Ldnull));
        il.Append(Instruction.Create(OpCodes.Stloc, request));
        il.Append(Instruction.Create(OpCodes.Ldarg_0));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_3));
        il.Append(Instruction.Create(OpCodes.Ldarg_1));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Ldnull));
        il.Append(Instruction.Create(OpCodes.Call, findHandler));
        il.Append(Instruction.Create(OpCodes.Stloc, handler));
        il.Append(Instruction.Create(OpCodes.Ldloc, handler));
        il.Append(Instruction.Create(OpCodes.Ldnull));
        il.Append(Instruction.Create(OpCodes.Call, equality));
        il.Append(Instruction.Create(OpCodes.Brfalse_S, handlerFound));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
        il.Append(Instruction.Create(OpCodes.Ret));
        il.Append(handlerFound);
        il.Append(Instruction.Create(OpCodes.Pop));
        il.Append(loadExecution);
        il.Append(Instruction.Create(OpCodes.Call, getService));
        il.Append(Instruction.Create(OpCodes.Callvirt, getCallback));
        il.Append(Instruction.Create(OpCodes.Call, proxyMethod));
        il.Append(Instruction.Create(OpCodes.Ldloc, request));
        il.Append(Instruction.Create(OpCodes.Callvirt, formRun));
        if (!callbackReturnsVoid)
            il.Append(Instruction.Create(OpCodes.Pop));
        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        il.Append(Instruction.Create(OpCodes.Ret));
        return method;
    }

    private static TypeDefinition AddType(ModuleDefinition module, string ns, string name)
    {
        var type = new TypeDefinition(ns, name, TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(type);
        return type;
    }

    private static MethodDefinition AddMethod(
        TypeDefinition type,
        string name,
        TypeReference returnType,
        params TypeReference[] parameterTypes)
    {
        var method = new MethodDefinition(name, MethodAttributes.Public, returnType);
        foreach (var parameterType in parameterTypes)
            method.Parameters.Add(new ParameterDefinition(parameterType));
        type.Methods.Add(method);
        return method;
    }
}
