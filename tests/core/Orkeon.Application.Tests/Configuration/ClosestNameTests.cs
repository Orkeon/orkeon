using Orkeon.Application.Configuration;

namespace Orkeon.Application.Tests.Configuration;

public class ClosestNameTests
{
    [Theory]
    [InlineData("temprature", "temperature", "temperature,top_p")]
    [InlineData("TEMPERATURE", "temperature", "temperature,top_p")]
    [InlineData("proces", "process", "goal,process,agents")]
    [InlineData("tpo_p", "top_p", "temperature,top_p")]
    [InlineData("ab", "aa", "aa,ac")]
    [InlineData("zzzzzz", null, "temperature,top_p")]
    [InlineData("anything", null, "")]
    [InlineData("max_tokn", "max_tokens", "max_tokens,min_tokens")]
    [Trait("BR", "CH-01")]
    public void ShouldProposeTheClosestKnownKey_WhenAKeyIsMisspelled(string written, string? expected, string known)
    {
        var names = known.Length == 0 ? [] : known.Split(',');

        var closest = ClosestName.Of(written, names);

        Assert.Equal(expected, closest);
    }
}
