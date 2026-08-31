using AlRunner.Infrastructure;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace AlRunner.Tests;

[Collection(BcEngineCollection.Name)]
public sealed class TaskSchedulerCecilRewriteTests
{
    private sealed record EntryPoint(
        string Name,
        string ReturnType,
        string HelperName,
        params string[] ParameterTypes)
    {
        public bool IsPublic { get; init; } = true;
    }

    private static readonly EntryPoint[] RewrittenEntryPoints =
    [
        new(
            "ALCreateTask",
            "System.Guid",
            "ALTaskScheduler_ALCreateTask",
            "System.Int32",
            "System.Int32",
            "System.Boolean",
            "System.String",
            "Microsoft.Dynamics.Nav.Runtime.NavDateTime",
            "Microsoft.Dynamics.Nav.Runtime.NavRecordId",
            "Microsoft.Dynamics.Nav.Runtime.NavDuration"),
        new(
            "ALTaskExists",
            "System.Boolean",
            "ALTaskScheduler_ALTaskExists",
            "System.Guid"),
        new(
            "ALCancelTask",
            "System.Boolean",
            "ALTaskScheduler_ALCancelTask",
            "System.Guid"),
        new(
            "ALSetTaskReady",
            "System.Boolean",
            "ALTaskScheduler_ALSetTaskReady",
            "System.Guid",
            "Microsoft.Dynamics.Nav.Runtime.NavDateTime"),
        new(
            "ALTaskExistsAsync",
            "System.Threading.Tasks.ValueTask`1<System.Boolean>",
            "ALTaskScheduler_ALTaskExistsAsync",
            "Microsoft.Dynamics.Nav.Runtime.NavSession",
            "System.Guid"),
        new(
            "ALCancelTaskAsync",
            "System.Threading.Tasks.ValueTask`1<System.Boolean>",
            "ALTaskScheduler_ALCancelTaskAsync",
            "Microsoft.Dynamics.Nav.Runtime.NavSession",
            "System.Guid"),
        new(
            "ALSetTaskReadyAsync",
            "System.Threading.Tasks.ValueTask`1<System.Boolean>",
            "ALTaskScheduler_ALSetTaskReadyAsync",
            "Microsoft.Dynamics.Nav.Runtime.NavSession",
            "System.Guid",
            "Microsoft.Dynamics.Nav.Runtime.NavDateTime"),
    ];

    private static readonly EntryPoint UntouchedCreateTaskAsync = new(
        "ALCreateTaskAsync",
        "System.Threading.Tasks.ValueTask`1<System.Guid>",
        "",
        "Microsoft.Dynamics.Nav.Runtime.NavSession",
        "System.Int32",
        "System.Int32",
        "System.Boolean",
        "System.String",
        "Microsoft.Dynamics.Nav.Runtime.NavDateTime",
        "Microsoft.Dynamics.Nav.Runtime.NavRecordId",
        "Microsoft.Dynamics.Nav.Runtime.NavDuration");

    private static readonly EntryPoint[] FalseEntryPoints =
    [
        new("ALCanCreateTask", "System.Boolean", ""),
        new(
            "ALCanCreateTask",
            "System.Boolean",
            "",
            "Microsoft.Dynamics.Nav.Runtime.NavSession"),
        new(
            "CanCreateTask",
            "System.Boolean",
            "",
            "Microsoft.Dynamics.Nav.Runtime.NavSession")
        {
            IsPublic = false,
        },
    ];

    private static readonly EntryPoint CheckCodeUnit = new(
        "CheckCodeUnit",
        "System.Void",
        "",
        "Microsoft.Dynamics.Nav.Runtime.NavSession",
        "System.Int32")
    {
        IsPublic = false,
    };

    [SkippableFact]
    public void FullRewrite_ForwardsTaskSchedulerEntryPointsToExactRunnerHelpers()
    {
        TestArtifacts.SkipIfMissing();
        var nclPath = Path.Combine(
            BcArtifacts.ServiceTierDir,
            "Microsoft.Dynamics.Nav.Ncl.dll");

        using var originalAssembly = AssemblyDefinition.ReadAssembly(nclPath);
        var originalTaskScheduler = originalAssembly.MainModule.GetType(
            "Microsoft.Dynamics.Nav.Runtime.ALTaskScheduler");
        Assert.NotNull(originalTaskScheduler);
        var originalCreateTaskAsync = NclCecilRewrite.ResolveTaskSchedulerEntryPoint(
            originalTaskScheduler,
            UntouchedCreateTaskAsync.Name,
            UntouchedCreateTaskAsync.ReturnType,
            UntouchedCreateTaskAsync.ParameterTypes);
        var originalMoveNext = ResolveMoveNext(originalCreateTaskAsync);

        var rewritten = NclCecilRewrite.RewriteNcl(nclPath);
        using var image = new MemoryStream(rewritten);
        using var assembly = AssemblyDefinition.ReadAssembly(image);
        var taskScheduler = assembly.MainModule.GetType(
            "Microsoft.Dynamics.Nav.Runtime.ALTaskScheduler");
        Assert.NotNull(taskScheduler);

        var expectedSurface = RewrittenEntryPoints
            .Append(UntouchedCreateTaskAsync)
            .Concat(FalseEntryPoints)
            .Append(CheckCodeUnit)
            .Select(DescribeSurface)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var relevantNames = expectedSurface
            .Select(signature => signature[(signature.IndexOf(' ') + 1)..signature.IndexOf('(')])
            .ToHashSet(StringComparer.Ordinal);
        var actualSurface = taskScheduler.Methods
            .Where(method => method.IsStatic && relevantNames.Contains(method.Name))
            .Select(DescribeSurface)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedSurface, actualSurface);

        foreach (var entryPoint in RewrittenEntryPoints)
        {
            var target = NclCecilRewrite.ResolveTaskSchedulerEntryPoint(
                taskScheduler,
                entryPoint.Name,
                entryPoint.ReturnType,
                entryPoint.ParameterTypes);
            Assert.Contains(NclCecilRewrite.Key(target), NclCecilRewrite.CecilOwned);
            AssertExactForwarder(target, entryPoint.HelperName);

            if (entryPoint.Name.EndsWith("Async", StringComparison.Ordinal))
                Assert.DoesNotContain(
                    target.CustomAttributes,
                    attribute => attribute.AttributeType.Name == "AsyncStateMachineAttribute");
        }

        foreach (var entryPoint in FalseEntryPoints)
        {
            var target = NclCecilRewrite.ResolveTaskSchedulerEntryPoint(
                taskScheduler,
                entryPoint.Name,
                entryPoint.IsPublic,
                entryPoint.ReturnType,
                entryPoint.ParameterTypes);
            Assert.Contains(NclCecilRewrite.Key(target), NclCecilRewrite.CecilOwned);
            Assert.Equal(new[] { OpCodes.Ldc_I4_0, OpCodes.Ret },
                target.Body.Instructions.Select(instruction => instruction.OpCode));
        }

        var checkCodeUnit = NclCecilRewrite.ResolveTaskSchedulerEntryPoint(
            taskScheduler,
            CheckCodeUnit.Name,
            CheckCodeUnit.IsPublic,
            CheckCodeUnit.ReturnType,
            CheckCodeUnit.ParameterTypes);
        Assert.Contains(NclCecilRewrite.Key(checkCodeUnit), NclCecilRewrite.CecilOwned);
        Assert.Equal(new[] { OpCodes.Ret },
            checkCodeUnit.Body.Instructions.Select(instruction => instruction.OpCode));

        var createTaskAsync = Assert.Single(
            taskScheduler.Methods,
            method => Describe(method) == Describe(UntouchedCreateTaskAsync));
        var moveNext = ResolveMoveNext(createTaskAsync);
        AssertMethodBodyUnchanged(originalCreateTaskAsync, createTaskAsync);
        AssertMethodBodyUnchanged(originalMoveNext, moveNext);
        Assert.Contains(
            moveNext.Body.Instructions,
            instruction => instruction.Operand is MethodReference method
                && method.DeclaringType.FullName == taskScheduler.FullName
                && method.Name == "CanCreateTask");
    }

    [SkippableFact]
    public void TaskSchedulerResolver_WithAmbiguousTarget_FailsLoudly()
    {
        TestArtifacts.SkipIfMissing();
        var nclPath = Path.Combine(
            BcArtifacts.ServiceTierDir,
            "Microsoft.Dynamics.Nav.Ncl.dll");
        using var assembly = AssemblyDefinition.ReadAssembly(nclPath);
        var taskScheduler = assembly.MainModule.GetType(
            "Microsoft.Dynamics.Nav.Runtime.ALTaskScheduler");
        Assert.NotNull(taskScheduler);
        var shape = RewrittenEntryPoints[0];
        var original = NclCecilRewrite.ResolveTaskSchedulerEntryPoint(
            taskScheduler,
            shape.Name,
            shape.ReturnType,
            shape.ParameterTypes);
        var duplicate = new MethodDefinition(
            original.Name,
            original.Attributes,
            original.ReturnType);
        foreach (var parameter in original.Parameters)
            duplicate.Parameters.Add(new ParameterDefinition(
                parameter.Name,
                parameter.Attributes,
                parameter.ParameterType));
        duplicate.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        taskScheduler.Methods.Add(duplicate);

        var error = Assert.Throws<InvalidOperationException>(() =>
            NclCecilRewrite.ResolveTaskSchedulerEntryPoint(
                taskScheduler,
                shape.Name,
                shape.ReturnType,
                shape.ParameterTypes));

        Assert.Contains("found 2", error.Message);
        Assert.Contains("do not commit", error.Message);
    }

    private static void AssertExactForwarder(MethodDefinition target, string helperName)
    {
        var instructions = target.Body.Instructions;
        Assert.Equal(target.Parameters.Count + 2, instructions.Count);
        for (var i = 0; i < target.Parameters.Count; i++)
        {
            Assert.Equal(OpCodes.Ldarg, instructions[i].OpCode);
            Assert.Same(target.Parameters[i], instructions[i].Operand);
        }

        var helperCall = instructions[target.Parameters.Count];
        Assert.Equal(OpCodes.Call, helperCall.OpCode);
        var helper = Assert.IsAssignableFrom<MethodReference>(helperCall.Operand);
        Assert.Equal("AlRunner.BcRuntime", helper.DeclaringType.FullName);
        Assert.Equal(helperName, helper.Name);
        Assert.Equal(target.ReturnType.FullName, helper.ReturnType.FullName);
        Assert.Equal(
            target.Parameters.Select(parameter => parameter.ParameterType.FullName),
            helper.Parameters.Select(parameter => parameter.ParameterType.FullName));
        Assert.Equal(OpCodes.Ret, instructions[target.Parameters.Count + 1].OpCode);
    }

    private static MethodDefinition ResolveMoveNext(MethodDefinition asyncMethod)
    {
        var stateMachineAttribute = Assert.Single(
            asyncMethod.CustomAttributes,
            attribute => attribute.AttributeType.Name == "AsyncStateMachineAttribute");
        var stateMachineReference = Assert.IsAssignableFrom<TypeReference>(
            Assert.Single(stateMachineAttribute.ConstructorArguments).Value);
        var stateMachine = stateMachineReference.Resolve();
        Assert.NotNull(stateMachine);
        return Assert.Single(stateMachine.Methods, method => method.Name == "MoveNext");
    }

    private static void AssertMethodBodyUnchanged(
        MethodDefinition original,
        MethodDefinition rewritten)
    {
        Assert.Equal(original.Body.InitLocals, rewritten.Body.InitLocals);
        Assert.Equal(
            original.Body.Variables.Select(variable => variable.VariableType.FullName),
            rewritten.Body.Variables.Select(variable => variable.VariableType.FullName));
        Assert.Equal(
            original.Body.Instructions.Select(instruction => instruction.ToString()),
            rewritten.Body.Instructions.Select(instruction => instruction.ToString()));
        Assert.Equal(
            original.Body.ExceptionHandlers.Select(DescribeExceptionHandler),
            rewritten.Body.ExceptionHandlers.Select(DescribeExceptionHandler));
    }

    private static string DescribeExceptionHandler(ExceptionHandler handler) =>
        $"{handler.HandlerType}|{handler.CatchType?.FullName}|" +
        $"{handler.TryStart?.Offset}|{handler.TryEnd?.Offset}|" +
        $"{handler.HandlerStart?.Offset}|{handler.HandlerEnd?.Offset}|{handler.FilterStart?.Offset}";

    private static string Describe(EntryPoint entryPoint) =>
        $"{entryPoint.Name}({string.Join(",", entryPoint.ParameterTypes)})->{entryPoint.ReturnType}";

    private static string Describe(MethodDefinition method) =>
        $"{method.Name}({string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.FullName))})" +
        $"->{method.ReturnType.FullName}";

    private static string DescribeSurface(EntryPoint entryPoint) =>
        $"{(entryPoint.IsPublic ? "public" : "private")} {Describe(entryPoint)}";

    private static string DescribeSurface(MethodDefinition method) =>
        $"{(method.IsPublic ? "public" : method.IsPrivate ? "private" : "other")} {Describe(method)}";
}
