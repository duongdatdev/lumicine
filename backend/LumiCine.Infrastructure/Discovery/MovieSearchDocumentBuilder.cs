using System.Security.Cryptography;
using System.Text;
using LumiCine.Application.Discovery;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Composes the Vietnamese catalog text embedded per movie and hashes it, so catalog changes
/// that alter the text also change the hash and re-queue the document.
/// </summary>
public sealed class MovieSearchDocumentBuilder(IOptions<GeminiOptions> geminiOptions) : IMovieSearchDocumentBuilder
{
    private const string SynopsisPrefix = "Nội dung: ";

    public string BuildDocumentText(string title, string? originalTitle, string? synopsis, IReadOnlyList<string> genreNames)
    {
        var cap = Math.Max(1, geminiOptions.Value.DocumentMaxCharacters);
        var lines = new List<string> { Collapse($"Tiêu đề: {title}") };
        if (!string.IsNullOrWhiteSpace(originalTitle))
            lines.Add(Collapse($"Tên gốc: {originalTitle}"));
        var genreList = string.Join(", ", genreNames.Where(name => !string.IsNullOrWhiteSpace(name)));
        if (genreList.Length > 0)
            lines.Add(Collapse($"Thể loại: {genreList}"));
        var fixedLength = lines.Sum(line => line.Length + 1);
        var synopsisContent = string.IsNullOrWhiteSpace(synopsis) ? null : Collapse(synopsis);
        if (synopsisContent is not null && cap > fixedLength + SynopsisPrefix.Length)
        {
            // Only the synopsis is trimmed to fit; the identifying lines stay intact.
            var budget = cap - fixedLength - SynopsisPrefix.Length;
            lines.Add(SynopsisPrefix + synopsisContent[..Math.Min(synopsisContent.Length, budget)]);
        }
        var text = string.Join('\n', lines);
        return text.Length <= cap ? text : text[..cap];
    }

    public string ComputeSourceHash(string documentText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(documentText)));

    private static string Collapse(string line)
    {
        var builder = new StringBuilder(line.Length);
        var previousWasSpace = false;
        foreach (var character in line)
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace)
            {
                if (!previousWasSpace) builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }
            previousWasSpace = isSpace;
        }
        return builder.ToString().Trim();
    }
}
