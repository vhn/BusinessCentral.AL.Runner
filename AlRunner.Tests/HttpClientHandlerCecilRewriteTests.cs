using AlRunner.Infrastructure;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace AlRunner.Tests;

public sealed class HttpClientHandlerCecilRewriteTests
{
    private const string ExternalHttpMessage =
        "out-of-scope: HttpClient.Send — external-http — see docs/scope.md#external-http";

    [SkippableFact]
    public void FullRewrite_PreservesAlEntryPointsAndMakesDispatcherMockOrOos()
    {
        TestArtifacts.SkipIfMissing();
        var nclPath = Path.Combine(
            BcArtifacts.ServiceTierDir,
            "Microsoft.Dynamics.Nav.Ncl.dll");

        var rewritten = NclCecilRewrite.RewriteNcl(nclPath);
        using var image = new MemoryStream(rewritten);
        using var assembly = AssemblyDefinition.ReadAssembly(image);
        var module = assembly.MainModule;
        var httpClient = module.GetType("Microsoft.Dynamics.Nav.Runtime.NavHttpClient");
        Assert.NotNull(httpClient);

        var alGet = Assert.Single(
            httpClient.Methods,
            method => method.Name == "ALGet" && method.HasBody);
        Assert.Contains(alGet.Body.Instructions, instruction =>
            instruction.Operand is MethodReference call
            && call.Name == "Get");

        var testExecution = module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution");
        Assert.NotNull(testExecution);
        var dispatcher = Assert.Single(
            testExecution.Methods,
            method => method.Name == "TestHandleHttpClientRequest"
                && method.Parameters.Count == 5
                && method.HasBody);

        var reachable = ReachableInstructions(dispatcher);
        Assert.Contains(reachable, instruction =>
            instruction.OpCode == OpCodes.Ldstr
            && Equals(instruction.Operand, ExternalHttpMessage));
        Assert.DoesNotContain(reachable, instruction =>
            IsFalseConstant(instruction)
            && instruction.Next?.OpCode == OpCodes.Ret);
        Assert.DoesNotContain(reachable, instruction =>
            instruction.Operand is MethodReference call
            && call.Name == "get_IsServiceRunningInLocalEnvironment");

        var successReturns = reachable.Where(instruction =>
            instruction.OpCode == OpCodes.Ret
            && IsTrueConstant(instruction.Previous)).ToArray();
        Assert.Single(successReturns);
    }

    [SkippableFact]
    public void DispatcherRewrite_WithAmbiguousTarget_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var testExecution = module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution");
        Assert.NotNull(testExecution);
        var original = Dispatcher(testExecution);
        var duplicate = new MethodDefinition(
            original.Name,
            Mono.Cecil.MethodAttributes.Assembly,
            module.TypeSystem.Boolean);
        foreach (var parameter in original.Parameters)
            duplicate.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
        duplicate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldc_I4_1));
        duplicate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        testExecution.Methods.Add(duplicate);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleHttpClientRequest(
                module,
                OosConstructor(module)));

        Assert.Contains("found 2", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void DispatcherRewrite_WithMissingTopologyGate_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var dispatcher = Dispatcher(module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution"));
        var topologyCheck = dispatcher.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference call
            && call.Name == "get_IsServiceRunningInLocalEnvironment");
        dispatcher.Body.GetILProcessor().Remove(topologyCheck);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleHttpClientRequest(
                module,
                OosConstructor(module)));

        Assert.Contains("local-environment checks", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void DispatcherRewrite_WithChangedHandlerResultBranch_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var dispatcher = Dispatcher(module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution"));
        var invokeHandler = dispatcher.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference call
            && call.Name == "InvokeHandler");
        invokeHandler.Next!.Next!.OpCode = OpCodes.Brtrue_S;

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleHttpClientRequest(
                module,
                OosConstructor(module)));

        Assert.Contains("mocked-response branch", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void DispatcherRewrite_WithChangedHandlerNullBranch_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var dispatcher = Dispatcher(module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution"));
        var findHandler = dispatcher.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference call
            && call.Name == "FindHandler"
            && call.Parameters.Count == 4);
        findHandler.Next!.Next!.Next!.Next!.Next!.OpCode = OpCodes.Brfalse_S;

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleHttpClientRequest(
                module,
                OosConstructor(module)));

        Assert.Contains("handler-null branch", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void DispatcherRewrite_WithWrongMockResponseOwner_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var dispatcher = Dispatcher(module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution"));
        var invokeHandler = dispatcher.Body.Instructions.Single(instruction =>
            instruction.Operand is MethodReference call
            && call.Name == "InvokeHandler");
        var mockedResponseStart = Assert.IsType<Instruction>(
            invokeHandler.Next!.Next!.Operand);
        mockedResponseStart.Next!.OpCode = OpCodes.Ldloc_1;
        mockedResponseStart.Next.Operand = null;

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteTestHandleHttpClientRequest(
                module,
                OosConstructor(module)));

        Assert.Contains("mocked-response branch", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void CallGraphValidation_WithInvertedHandledBranch_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var httpClient = HttpClient(module);
        var dispatcher = Dispatcher(module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution"));
        var sendRequest = Assert.Single(
            httpClient.Methods,
            method => method.Name == "SendRequestAsync"
                && method.Parameters.Count == 5);
        var dispatcherCall = Assert.Single(
            sendRequest.Body.Instructions,
            instruction => instruction.Operand is MethodReference call
                && call.FullName == dispatcher.FullName);
        dispatcherCall.Next!.OpCode = OpCodes.Brtrue_S;

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.ValidateNavHttpClientEgressCallGraph(
                module,
                httpClient,
                dispatcher));

        Assert.Contains("false-result branch", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void CallGraphValidation_WithNonTailRequestWrapper_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var httpClient = HttpClient(module);
        var testExecution = module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution");
        Assert.NotNull(testExecution);
        var dispatcher = Dispatcher(testExecution);
        var wrapper = Assert.Single(
            testExecution.Methods,
            method => method.Name == "TestHandleHttpClientRequest"
                && method.Parameters.Count == 3
                && method.HasBody);
        var dispatcherCall = Assert.Single(
            wrapper.Body.Instructions,
            instruction => instruction.Operand is MethodReference call
                && call.FullName == dispatcher.FullName);
        wrapper.Body.GetILProcessor().InsertAfter(
            dispatcherCall,
            Instruction.Create(OpCodes.Nop));

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.ValidateNavHttpClientEgressCallGraph(
                module,
                httpClient,
                dispatcher));

        Assert.Contains("tail-calls", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void CallGraphValidation_WithWrongAlForwarder_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var httpClient = HttpClient(module);
        var dispatcher = Dispatcher(module.GetType(
            "Microsoft.Dynamics.Nav.Runtime.NavTestExecution"));
        var alPost = Assert.Single(
            httpClient.Methods,
            method => method.Name == "ALPost" && method.HasBody);
        var postCall = Assert.Single(
            alPost.Body.Instructions,
            instruction => instruction.Operand is MethodReference call
                && call.DeclaringType.FullName == httpClient.FullName);
        postCall.Operand = Assert.Single(
            httpClient.Methods,
            method => method.Name == "Put"
                && method.Parameters.Count == 4);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.ValidateNavHttpClientEgressCallGraph(
                module,
                httpClient,
                dispatcher));

        Assert.Contains("ALPost", error.Message);
        Assert.Contains("forwards directly", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    [SkippableFact]
    public void RequiredTargetGetter_WithAmbiguousGetter_FailsLoudly()
    {
        using var assembly = ReadPristineNcl();
        var module = assembly.MainModule;
        var httpClient = HttpClient(module);
        var original = Assert.Single(
            httpClient.Methods,
            method => method.Name == "get_Target"
                && method.Parameters.Count == 0);
        var duplicate = new MethodDefinition(
            original.Name,
            Mono.Cecil.MethodAttributes.Public,
            original.ReturnType);
        duplicate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldnull));
        duplicate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        httpClient.Methods.Add(duplicate);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.RewriteRequiredHttpTargetGetter(
                module,
                httpClient,
                "Microsoft.Dynamics.Nav.Runtime.SharedNavHttpClient",
                nameof(AlRunner.BcRuntime.NavHttpClient_get_Target)));

        Assert.Contains("found 2", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    private static AssemblyDefinition ReadPristineNcl()
    {
        TestArtifacts.SkipIfMissing();
        var path = Path.Combine(
            BcArtifacts.ServiceTierDir,
            "Microsoft.Dynamics.Nav.Ncl.dll");
        return AssemblyDefinition.ReadAssembly(path);
    }

    private static MethodDefinition Dispatcher(TypeDefinition testExecution)
        => Assert.Single(
            testExecution.Methods,
            method => method.Name == "TestHandleHttpClientRequest"
                && method.Parameters.Count == 5
                && method.HasBody);

    private static TypeDefinition HttpClient(ModuleDefinition module)
    {
        var httpClient = module.GetType("Microsoft.Dynamics.Nav.Runtime.NavHttpClient");
        Assert.NotNull(httpClient);
        return httpClient;
    }

    private static MethodReference OosConstructor(ModuleDefinition module)
        => module.ImportReference(typeof(InvalidOperationException).GetConstructor(
            [typeof(string)])!);

    private static HashSet<Instruction> ReachableInstructions(MethodDefinition method)
    {
        var reachable = new HashSet<Instruction>();
        var pending = new Stack<Instruction>();
        pending.Push(method.Body.Instructions[0]);

        while (pending.TryPop(out var instruction))
        {
            if (!reachable.Add(instruction))
                continue;

            if (instruction.OpCode.FlowControl == FlowControl.Branch)
            {
                pending.Push((Instruction)instruction.Operand);
                continue;
            }

            if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
            {
                if (instruction.Operand is Instruction target)
                    pending.Push(target);
                else if (instruction.Operand is Instruction[] targets)
                    foreach (var branchTarget in targets)
                        pending.Push(branchTarget);
            }

            if (instruction.OpCode.FlowControl is not FlowControl.Return
                and not FlowControl.Throw
                && instruction.Next != null)
                pending.Push(instruction.Next);
        }

        return reachable;
    }

    private static bool IsFalseConstant(Instruction? instruction)
        => instruction?.OpCode == OpCodes.Ldc_I4_0;

    private static bool IsTrueConstant(Instruction? instruction)
        => instruction?.OpCode == OpCodes.Ldc_I4_1;
}
