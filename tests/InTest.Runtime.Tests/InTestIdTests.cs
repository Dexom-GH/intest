using Shouldly;

namespace InTest.Runtime.Tests;

[TestClass]
public class InTestIdTests
{
    private const string Run = "tjay-20260817T142233Z-a3f91c2e";

    [TestMethod]
    public void ForTest_LosslessNameProducesReadableSlug()
    {
        InTestId.ForTest(Run, "GetOrderById_Contract")
                .ShouldBe($"{Run}-getorderbyid-contract");
    }

    [TestMethod]
    [DataRow("quantity = -1 \u2192 400", DisplayName = "arrow")]
    [DataRow("notes = \U0001F600", DisplayName = "emoji")]
    [DataRow("notes = \u05D0\u05D1", DisplayName = "RTL")]
    public void ForTest_IsAlwaysAscii(string displayName)
    {
        var id = InTestId.ForTest(Run, displayName);
        id.ShouldAllBe(c => c < 128);
    }

    [TestMethod]
    public void ForTest_LossyNamesDoNotCollide()
    {
        var emoji = InTestId.ForTest(Run, "notes = \U0001F600");
        var rtl = InTestId.ForTest(Run, "notes = \u05D0\u05D1");
        emoji.ShouldNotBe(rtl);
    }

    [TestMethod]
    public void ForTest_IsStableAcrossCalls()
    {
        InTestId.ForTest(Run, "notes = \U0001F600")
                .ShouldBe(InTestId.ForTest(Run, "notes = \U0001F600"));
    }

    [TestMethod]
    public void ForTest_RespectsLengthCap()
    {
        InTestId.ForTest(Run, new string('x', 500)).Length.ShouldBeLessThanOrEqualTo(120);
    }

    [TestMethod]
    public void ForTest_ResultIsAcceptedAsAHeaderValue()
    {
        using var request = new HttpRequestMessage();
        Should.NotThrow(() => request.Headers.Add("X-Test-Run-Id", InTestId.ForTest(Run, "quantity = -1 \u2192 400")));
    }
}
