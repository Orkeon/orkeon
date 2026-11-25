using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Hosting.Conversations;

/// <summary>What <c>chat_attach</c> is asked.</summary>
internal sealed record ChatAttachRequest
{
    /// <summary>The files to send.</summary>
    [FieldSchema(Description = "Virtual paths of the files to send: under /outbox, or under a folder this crew opens to download. Never /inbox.", IsRequired = true, ItemsType = "string", Example = "[\"/outbox/report.pdf\"]")]
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>What <c>chat_attach</c> answers.</summary>
internal sealed record ChatAttachResponse
{
    /// <summary>Whether a file was sent, or waits for the person.</summary>
    [ReturnSchema(Description = "Whether at least one file was sent or awaits consent")]
    public bool Success { get; init; }

    /// <summary>Why none was.</summary>
    [ReturnSchema(Description = "Why no file was sent")]
    public string? Error { get; init; }

    /// <summary>What became of each file.</summary>
    [ReturnSchema(Description = "Each file with its status: sent, too_large, refused or awaiting_consent, and the reason when it was not sent")]
    public IReadOnlyList<AttachedFile> Files { get; init; } = [];
}

/// <summary>Sends files to the chat conversation the run answers. Registered wherever a crew loads, refused outside a conversation.</summary>
[ToolContract("chat_attach", StaysWithCaller = true,
    Name = "chat_attach",
    Description = "Send files to the chat conversation this run answers, and to no other. A file must be under /outbox, or under a folder the crew opens to download. Outside a chat conversation the call fails. What was sent cannot be taken back, and the number of files and messages of a run is bounded: send the finished file, once.",
    Category = "Interaction")]
internal sealed class ChatAttachTool : ToolBase<ChatAttachRequest, ChatAttachResponse>
{
    internal const int MaxPaths = 50;

    internal const string NoConversation =
        "This run has no chat conversation to send files to, or it is over. Leave the files where they are, and name them in the answer.";

    /// <summary>Creates the tool.</summary>
    public ChatAttachTool(ILogger<ChatAttachTool>? logger = null)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Execute;

    /// <inheritdoc />
    public override bool UsesFileSystem => true;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ChatAttachRequest request) =>
        request?.Paths.Count switch
        {
            null or 0 => "`paths` names no file",
            > MaxPaths => $"`paths` names more than {MaxPaths} files",
            _ => null,
        };

    /// <inheritdoc />
    protected override async Task<ChatAttachResponse> ExecuteTypedAsync(ChatAttachRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ConversationScope.Current is not { IsOpen: true } conversation)
            return new ChatAttachResponse { Error = NoConversation };

        var files = await conversation.AttachAsync(request.Paths, cancellationToken).ConfigureAwait(false);
        var sent = files.Any(f => f.Status is AttachedFileStatus.Sent or AttachedFileStatus.AwaitingConsent);
        return new ChatAttachResponse
        {
            Success = sent,
            Error = sent ? null : "No file was sent. " + string.Join("; ", files.Select(f => $"{f.Path}: {f.Status} ({f.Reason})")),
            Files = files,
        };
    }
}

/// <summary>The registration of <c>chat_attach</c>.</summary>
internal static class ChatAttachExtensions
{
    /// <summary>Registers the tool, once: wherever a crew is loaded, so it loads everywhere.</summary>
    public static IServiceCollection AddOrkeonChatAttach(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, ChatAttachTool>());
        return services;
    }
}
