using System.Text.RegularExpressions;

namespace HC.AiProcessor.Application.Helpers;

public static partial class FileHelper
{
    public static string GenerateUniqueFileName(string fileName)
    {
        const int maxLength = 50;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

        var sanitizedFileName = SpacesRegex().Replace(fileNameWithoutExtension, "-");
        var sanitizedFileNameLength = sanitizedFileName.Length < maxLength ? sanitizedFileName.Length : maxLength;
        var sanitizedFileNameLimited = sanitizedFileName[..sanitizedFileNameLength];

        return $"{sanitizedFileNameLimited}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
