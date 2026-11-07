using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Fallback;

/// <summary>FWK-05: the <c>Fallback</c> and <c>FallbackBudgetSeconds</c> keys of <c>Llm:Profiles:&lt;name&gt;</c>.</summary>
public sealed class LlmProfileFallbackSettingsTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => KeyValuePair.Create<string, string?>(p.Key, p.Value))).Build();

    private static LlmProfileRegistration Registered(IConfiguration configuration, string name)
    {
        var services = new ServiceCollection();
        services.AddOrkeonLlmProfiles(configuration);
        using var container = services.BuildServiceProvider();
        return container.GetServices<LlmProfileRegistration>().Single(r => r.Name == name);
    }

    [Fact]
    public void A_list_of_names_and_a_budget_reach_the_registration()
    {
        var registration = Registered(Settings(
            ("Llm:Profiles:a:Provider", "mock"), ("Llm:Profiles:a:Responses", "/llm-mock/a.jsonl"),
            ("Llm:Profiles:a:Fallback:0", "b"), ("Llm:Profiles:a:Fallback:1", "default"),
            ("Llm:Profiles:a:FallbackBudgetSeconds", "45"),
            ("Llm:Profiles:b:Provider", "mock"), ("Llm:Profiles:b:Responses", "/llm-mock/b.jsonl")), "a");

        Assert.Equal(["b", "default"], registration.Fallback);
        Assert.Equal(TimeSpan.FromSeconds(45), registration.FallbackBudget);
    }

    [Fact]
    public void One_comma_separated_value_is_a_list()
    {
        var registration = Registered(Settings(
            ("Llm:Profiles:a:Provider", "mock"), ("Llm:Profiles:a:Responses", "/llm-mock/a.jsonl"),
            ("Llm:Profiles:a:Fallback", "b, c")), "a");

        Assert.Equal(["b", "c"], registration.Fallback);
        Assert.Null(registration.FallbackBudget);
    }

    [Fact]
    public void A_profile_that_names_no_fallback_has_none()
    {
        var registration = Registered(Settings(
            ("Llm:Profiles:a:Provider", "mock"), ("Llm:Profiles:a:Responses", "/llm-mock/a.jsonl")), "a");

        Assert.True(registration.Fallback is null or { Count: 0 });
        Assert.Null(registration.FallbackBudget);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void A_budget_that_is_not_above_zero_refuses_the_host_naming_the_key(string budget)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddOrkeonLlmProfiles(Settings(
            ("Llm:Profiles:a:Provider", "mock"), ("Llm:Profiles:a:Responses", "/llm-mock/a.jsonl"),
            ("Llm:Profiles:a:Fallback", "b"), ("Llm:Profiles:a:FallbackBudgetSeconds", budget))));

        Assert.Contains("Llm:Profiles:a:FallbackBudgetSeconds", ex.Message, StringComparison.Ordinal);
    }
}
