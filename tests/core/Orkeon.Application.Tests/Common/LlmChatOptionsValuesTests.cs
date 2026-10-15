using System.Globalization;
using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Tests.Common;

public class LlmChatOptionsValuesTests
{
    [Theory]
    [Trait("BR", "CH-05")]
    [InlineData(0.7f, 0.7)]
    [InlineData(1.05f, 1.05)]
    [InlineData(0.30000001f, 0.3)]
    [InlineData(null, null)]
    public void ShouldReadTheShortestDecimal_WhenAFloatIsGiven(float? value, double? expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Assert.Equal(expected, LlmChatOptionsValues.AsWritten(value));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
