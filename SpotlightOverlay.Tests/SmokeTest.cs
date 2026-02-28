using Xunit;

namespace SpotlightOverlay.Tests;

public class SmokeTest
{
    [Fact]
    public void ProjectReferencesWork()
    {
        // Verify the test project can reference the main project
        Assert.NotNull(typeof(App));
    }
}
