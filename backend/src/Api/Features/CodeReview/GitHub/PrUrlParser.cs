using System.Text.RegularExpressions;

namespace Api.Features.CodeReview.GitHub;

public static partial class PrUrlParser
{
    [GeneratedRegex(
        @"^https?://github\.com/(?<owner>[\w.\-]+)/(?<repo>[\w.\-]+)/pull/(?<number>\d+)(?:[/?#].*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    public static bool TryParse(string url, out PrCoordinates coordinates)
    {
        coordinates = default!;

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var match = UrlRegex().Match(url.Trim());
        if (!match.Success)
        {
            return false;
        }

        var number = int.Parse(match.Groups["number"].Value);
        if (number <= 0)
        {
            return false;
        }

        coordinates = new PrCoordinates(
            match.Groups["owner"].Value,
            match.Groups["repo"].Value,
            number);
        return true;
    }

    public static PrCoordinates Parse(string url)
    {
        if (!TryParse(url, out var coords))
        {
            throw new InvalidPrUrlException(url);
        }
        return coords;
    }
}
