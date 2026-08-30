using AlRunner.Patches;
using Xunit;

namespace AlRunner.Tests;

public sealed class BcAppSymbolCacheEnumDefaultImplementationTests
{
    [Fact]
    public void EnumValueWithoutOverride_UsesEnumDefaultImplementation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "EnumTypes": [
                    {
                      "Id": 204,
                      "Name": "Alt. Cust. VAT Reg. Consist.",
                      "ImplementedInterfaces": ["\"Alt. Cust. VAT Reg. Consist.\""],
                      "Properties": [
                        { "Name": "DefaultImplementation", "Value": "204" }
                      ],
                      "Values": [
                        { "Name": "Default" }
                      ]
                    }
                  ]
                }
                """);

            var enumSymbol = Assert.Single(BcAppSymbolCache.GetFromJson(path).Enums);

            Assert.Equal(new[] { 204 }, Assert.Single(enumSymbol.Implementations));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
