using System.Security.Cryptography;
using System.Text;

namespace Kododo.Polyglot.Web.Api;

/// <summary>
/// Versions a response by its content: SHA-256 over the canonical parts, shortened to 16 hex
/// characters. It is sent as the ETag, so a client that already has it can be answered with an empty
/// 304.
/// </summary>
public static class ContentVersion
{
    public static string Of(IEnumerable<string> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var part in parts)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(part));

            // A separator, so that ["ab", "c"] and ["a", "bc"] cannot hash alike.
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetCurrentHash())[..16];
    }
}
