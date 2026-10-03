using Orkeon.Infrastructure.Crew;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// The LLM a hierarchical or autonomous strategy built by hand gives its manager, in tests whose
/// manager is a double: <see cref="MockManagerAgent"/> records the <c>ManagerLlm</c> it is handed and
/// calls nothing. One shared chat client nothing is meant to call — it holds no resource to release.
/// </summary>
public static class TestManagerLlm
{
    private static readonly MockChatClient s_neverCalled = new();

    /// <summary>A resolver whose default profile is that chat client.</summary>
    public static ManagerLlmResolver Resolver() => new(s_neverCalled);
}
