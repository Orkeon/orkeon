using Microsoft.Extensions.Logging;
using MimeKit;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Security;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Parses an <c>.eml</c> file with the same reader as <c>email_read</c>.</summary>
[ToolContract("email_parser",
    Name = "email_parser",
    Description = "Parse an .eml file (RFC 5322 message) from a virtual path: headers, body as text in slices (continue with `offset` = `next_offset`), attachments, and a prompt-injection screening verdict. Same output as email_read.",
    Category = "Email")]
internal sealed class EmailParserTool : ToolBase<EmailParserRequest, EmailReadResponse>
{
    private readonly IFileSystemService _fileSystem;
    private readonly EmailContentScreen _screen;

    /// <summary>Creates the tool.</summary>
    public EmailParserTool(IFileSystemService fileSystem, EmailContentScreen screen, ILogger<EmailParserTool>? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(screen);
        _fileSystem = fileSystem;
        _screen = screen;
    }

    /// <inheritdoc />
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(EmailParserRequest request) =>
        string.IsNullOrWhiteSpace(request?.Path) ? "`path` is required: the virtual path of an .eml file" : null;

    /// <inheritdoc />
    protected override async Task<EmailReadResponse> ExecuteTypedAsync(EmailParserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.Path.Trim();
        var check = _fileSystem.ResolveAndValidate(path, FileAccessRights.Read);
        if (!check.IsAllowed)
            throw new EmailToolException(EmailErrorCode.InvalidRequest, $"Cannot read '{path}': {(check.DenialReason ?? "the path is not readable").TrimEnd('.')}.");

        var stream = await _fileSystem.OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            MimeMessage message;
            try
            {
                message = await MimeMessage.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (FormatException ex)
            {
                throw new EmailToolException(EmailErrorCode.InvalidRequest, $"'{path}' is not an e-mail message (.eml): {ex.Message}", ex);
            }

            using (message)
            {
                return EmailToolHelpers.BuildRead(message, _screen, request.Offset, request.MaxChars, folder: path);
            }
        }
    }
}
