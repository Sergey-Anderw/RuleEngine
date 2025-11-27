namespace HC.AiProcessor.Application.Helpers;

public static class StringHelpers
{
    public static string MaskSensitiveData(
        string data,
        int unmaskedPrefix = 4,
        int unmaskedSuffix = 4,
        char maskChar = '*')
    {
        if (data.Length <= unmaskedPrefix + unmaskedSuffix)
            return new string(maskChar, data.Length);

        int maskLength = data.Length - unmaskedPrefix - unmaskedSuffix;
        var maskedPart = new string(maskChar, maskLength);

        return string.Concat(
            data.AsSpan()[..unmaskedPrefix],
            maskedPart,
            data.AsSpan(data.Length - unmaskedSuffix));
    }
}
