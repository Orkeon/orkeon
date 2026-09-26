using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Email.Auth;

namespace Orkeon.Tools.Email.Tests.Auth;

/// <summary>OAuth tokens kept as one JSON file per account under a virtual directory.</summary>
public sealed class FileSystemEmailTokenStoreTests
{
    private static readonly EmailTokenSet Tokens = new()
    {
        AccessToken = "ya29.access",
        RefreshToken = "1//refresh",
        ExpiresAt = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero),
        Scopes = ["https://mail.google.com/"],
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_write_one_JSON_file_per_key_and_read_it_back()
    {
        var files = new FakeFileSystemService().AddMount("/credentials", FileAccessRights.ReadWrite);
        var store = new FileSystemEmailTokenStore(files, "/credentials/email/");

        await store.WriteAsync("perso-0123456789ab", Tokens, Token);
        var read = await store.ReadAsync("perso-0123456789ab", Token);

        var json = await files.TryReadAllTextAsync("/credentials/email/perso-0123456789ab.json", Token);
        Assert.NotNull(json);
        Assert.Contains("\"AccessToken\": \"ya29.access\"", json, StringComparison.Ordinal);
        Assert.NotNull(read);
        Assert.Equal((Tokens.AccessToken, Tokens.RefreshToken, Tokens.ExpiresAt), (read.AccessToken, read.RefreshToken, read.ExpiresAt));
        Assert.Equal(Tokens.Scopes, read.Scopes);
        Assert.True(await files.ExistsAsync("/credentials/email", Token));
    }

    [Fact]
    public async Task Should_replace_the_tokens_of_a_key()
    {
        var store = Store(out _);
        await store.WriteAsync("perso", Tokens, Token);

        await store.WriteAsync("perso", Tokens with { AccessToken = "ya29.newer" }, Token);

        Assert.Equal("ya29.newer", (await store.ReadAsync("perso", Token))?.AccessToken);
    }

    [Fact]
    public async Task Should_answer_null_for_a_missing_blank_or_damaged_file()
    {
        var store = Store(out var files);
        files.AddFile("/credentials/email/blank.json", "   ");
        files.AddFile("/credentials/email/damaged.json", "{ \"AccessToken\": ");

        Assert.Null(await store.ReadAsync("missing", Token));
        Assert.Null(await store.ReadAsync("blank", Token));
        Assert.Null(await store.ReadAsync("damaged", Token));
    }

    [Fact]
    public async Task Should_delete_the_file_and_say_whether_there_was_one()
    {
        var store = Store(out var files);
        await store.WriteAsync("perso", Tokens, Token);

        Assert.True(await store.DeleteAsync("perso", Token));
        Assert.False(await store.DeleteAsync("perso", Token));
        Assert.False(await files.ExistsAsync("/credentials/email/perso.json", Token));
        Assert.Null(await store.ReadAsync("perso", Token));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("with space")]
    [InlineData("   ")]
    public async Task Should_refuse_a_key_that_could_leave_the_directory_or_hide_a_file(string key)
    {
        var store = Store(out _);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ReadAsync(key, Token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.WriteAsync(key, Tokens, Token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DeleteAsync(key, Token));
    }

    [Fact]
    public void Should_require_a_file_system_and_a_directory()
    {
        Assert.Throws<ArgumentNullException>(() => new FileSystemEmailTokenStore(null!, "/credentials"));
        Assert.Throws<ArgumentException>(() => new FileSystemEmailTokenStore(new FakeFileSystemService(), " "));
    }

    private static FileSystemEmailTokenStore Store(out FakeFileSystemService files)
    {
        files = new FakeFileSystemService().AddMount("/credentials", FileAccessRights.ReadWrite);
        return new FileSystemEmailTokenStore(files, "/credentials/email");
    }
}
