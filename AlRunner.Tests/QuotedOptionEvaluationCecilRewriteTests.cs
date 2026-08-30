using AlRunner.Infrastructure;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace AlRunner.Tests;

public sealed class QuotedOptionEvaluationCecilRewriteTests
{
    [Theory]
    [InlineData("\"GL Payment\"", "Item,GL Payment", "GL Payment")]
    [InlineData("\"A|B\"", "A|B,C", "A|B")]
    [InlineData("\"A\"\"B\"", "A\"B,C", "A\"B")]
    [InlineData("''", " ,Customer,Vendor", " ")]
    [InlineData("''", ",Customer,Vendor", "")]
    [InlineData("Rounding", "Item,Rounding", "Rounding")]
    [InlineData("Wallet", "WALLET,TICKET,COUPON", "WALLET")]
    public void NormalizeQuotedOptionValue_RemovesOnlyIdentifierQuotes(
        string source,
        string optionString,
        string expected)
    {
        Assert.Equal(
            expected,
            BcRuntime.NormalizeQuotedOptionValueForMetadata(source, optionString));
    }

    [Fact]
    public void NormalizeOptionCaption_ReturnsTheCorrespondingRuntimeMember()
    {
        Assert.Equal(
            "EXTERNALTICKETNO",
            BcRuntime.NormalizeQuotedOptionValueForMetadata(
                "Ticket No.",
                "EXTERNALTICKETNO",
                ["EXTERNALTICKETNO"],
                isEnum: false,
                optionCaptions: ["Ticket No."]));
    }

    [Fact]
    public void InternalEvaluate_NormalizesSourceBeforeTheOriginalBody()
    {
        using var module = ModuleDefinition.CreateModule("SyntheticNcl", ModuleKind.Dll);
        var type = new TypeDefinition(
            "Microsoft.Dynamics.Nav.Runtime",
            "NavOptionEvaluator",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition(
            "InternalEvaluate",
            MethodAttributes.Private,
            module.TypeSystem.Boolean);
        type.Methods.Add(method);
        for (var i = 0; i < 6; i++)
            method.Parameters.Add(new ParameterDefinition(
                i == 4 ? "source" : $"arg{i}",
                ParameterAttributes.None,
                i == 4 ? module.TypeSystem.String : module.TypeSystem.Object));
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Ret));

        NclCecilRewrite.RewriteQuotedOptionEvaluation(module);

        Assert.Equal(OpCodes.Ldarg, method.Body.Instructions[0].OpCode);
        Assert.Same(method.Parameters[3], method.Body.Instructions[0].Operand);
        Assert.Equal(OpCodes.Ldarg, method.Body.Instructions[1].OpCode);
        Assert.Same(method.Parameters[4], method.Body.Instructions[1].Operand);
        Assert.Equal(OpCodes.Call, method.Body.Instructions[2].OpCode);
        Assert.Contains(
            nameof(BcRuntime.NormalizeQuotedOptionValue),
            method.Body.Instructions[2].Operand!.ToString());
        Assert.Equal(OpCodes.Starg, method.Body.Instructions[3].OpCode);
        Assert.Same(method.Parameters[4], method.Body.Instructions[3].Operand);
        Assert.Equal(OpCodes.Ldc_I4_0, method.Body.Instructions[4].OpCode);
    }
}
