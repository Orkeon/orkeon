using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// VFS-90: the identity of a settings mount entry. <c>Ulid.Parse</c> is lenient — a letter
/// outside the Crockford alphabet decodes into another id, the all-zero id parses — so the
/// shape is checked first: a typo must be refused, never silently point at another entry.
/// </summary>
public class MountIdTests
{
    private const string Valid = "01J9Z3K4M5N6P7Q8R9S0T1V2W3";

    [Fact]
    public void A_well_formed_id_parses_and_reads_back_upper_case()
    {
        Assert.True(MountId.TryParse(Valid, out var id));
        Assert.Equal(Valid, id!.ToString());

        Assert.True(MountId.TryParse(Valid.ToLowerInvariant(), out var lower));
        Assert.Equal(id, lower);
        Assert.Equal(Valid, lower!.ToString());
    }

    [Fact]
    public void Whitespace_around_an_id_is_tolerated()
    {
        Assert.True(MountId.TryParse($"  {Valid} ", out var id));
        Assert.Equal(Valid, id!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W")]     // 25 characters
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W34")]   // 27 characters
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2WI")]    // I is not Crockford
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2WL")]    // L is not Crockford
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2WO")]    // O is not Crockford
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2WU")]    // U is not Crockford
    [InlineData("8ZZZZZZZZZZZZZZZZZZZZZZZZZ")]    // first character above 7 overflows
    [InlineData("00000000000000000000000000")]    // the empty ULID
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2Wé")]    // not Latin
    [InlineData("/output")]
    public void Anything_else_is_refused_rather_than_decoded_into_another_id(string? text)
    {
        Assert.False(MountId.TryParse(text, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void Create_mints_an_id_the_parser_accepts()
    {
        var minted = MountId.Create();

        Assert.Equal(MountId.Length, minted.ToString().Length);
        Assert.True(MountId.TryParse(minted.ToString(), out var parsed));
        Assert.Equal(minted, parsed);
    }
}
