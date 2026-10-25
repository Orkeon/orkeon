using System.Security.Cryptography;
using System.Text;

namespace Orkeon.Infrastructure.Session;

/// <summary>The folder name derived from a project key, shared by the session buffer and the agent memory.</summary>
public static class ProjectKey
{
    /// <summary>
    /// <c>&lt;name&gt;-&lt;hash&gt;</c> of a project key: the name is the key's last segment reduced to
    /// letters, digits and dashes, the hash (of the whole key) keeps two projects apart.
    /// </summary>
    public static string Slug(string projectKey)
    {
        ArgumentNullException.ThrowIfNull(projectKey);
        var name = new string(projectKey.TrimEnd('/', '\\').Split(['/', '\\'])[^1]
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray());
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projectKey)))[..12];
        return $"{name}-{hash}";
    }
}
