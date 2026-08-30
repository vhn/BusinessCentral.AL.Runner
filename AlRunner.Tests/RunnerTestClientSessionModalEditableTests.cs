using AlRunner;
using Xunit;

namespace AlRunner.Tests;

public class RunnerTestClientSessionModalEditableTests
{
    [Fact]
    public void ModalPageCarriesTheLiveFormsReadOnlyStateIntoTheTestPage()
    {
        var page = new LiveNavTestPage(
            record: null,
            controlIdToFieldNo: new Dictionary<int, int>(),
            creatable: true);

        page.MarkModalOpened(editable: false);

        Assert.True(page.IsOpened());
        Assert.False(page.RuntimeEditable);
    }
}
