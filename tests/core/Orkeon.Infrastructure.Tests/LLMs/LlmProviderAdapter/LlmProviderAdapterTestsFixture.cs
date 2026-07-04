namespace Orkeon.Infrastructure.Tests.LLMs;

public class LlmProviderAdapterTestsFixture
{
    private readonly string _name = "";
    private readonly bool _shouldThrowOnGenerate = false;
    private readonly string _responseContent = "";

    public LlmProviderAdapterTestsFixture()
    {
    }

    public LlmProviderAdapterTestsFixture WithName(string value)
    {
        // Configure _name as needed
        return this;
    }

    public LlmProviderAdapterTestsFixture WithShouldThrowOnGenerate(bool value)
    {
        // Configure _shouldThrowOnGenerate as needed
        return this;
    }

    public LlmProviderAdapterTestsFixture WithResponseContent(string value)
    {
        // Configure _responseContent as needed
        return this;
    }

    public string GetName() => _name;
    public bool GetShouldThrowOnGenerate() => _shouldThrowOnGenerate;
    public string GetResponseContent() => _responseContent;

}
