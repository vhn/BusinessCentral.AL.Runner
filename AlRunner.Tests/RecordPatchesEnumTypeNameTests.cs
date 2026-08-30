using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class RecordPatchesEnumTypeNameTests
{
    [Theory]
    [InlineData("Enum \"NPR TM AdmitTicketOnEoSMode\"")]
    [InlineData("ENUM \"NPR TM AdmitTicketOnEoSMode\"")]
    [InlineData("enum \"NPR TM AdmitTicketOnEoSMode\"")]
    public void EnumKeyword_IsCaseInsensitive(string typeName)
    {
        Assert.Equal("NPR TM AdmitTicketOnEoSMode", RecordPatches.ParseEnumTypeName(typeName));
    }
}
