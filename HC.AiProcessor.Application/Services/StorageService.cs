using System.Text.RegularExpressions;
using HC.Packages.Storage;

namespace HC.AiProcessor.Application.Services;

public interface IStorageService
{
    Task<string> SaveTempFileAsync(
        Stream file,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default
    );
}

public partial class StorageService(IStorageProvider storageProvider) : IStorageService
{
    public async Task<string> SaveTempFileAsync(
        Stream file,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default
    )
    {
        var bucket = await CreateBucketIfNotExist(BucketTypeEnum.Temporary, cancellationToken: cancellationToken);
        await UploadFile(file, bucket, fileName, contentType, cancellationToken);

        return $"{bucket}/{fileName}";
    }

    private async Task UploadFile(
        Stream file,
        string bucket,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default
    )
    {
        await storageProvider.Upload(
            bucket,
            fileName,
            contentType,
            file,
            true,
            cancellationToken
        );
    }

    private async Task<string> CreateBucketIfNotExist(
        BucketTypeEnum bucketType,
        string? bucketPostfix = null,
        CancellationToken cancellationToken = default
    )
    {
        var bucketName = GetBucketName(bucketType, bucketPostfix);
        await storageProvider.CreateBucketIfNotExist(
            bucketName,
            false,
            cancellationToken
        );

        return bucketName;
    }

    private static string GetBucketName(BucketTypeEnum type, string? postfix)
    {
        var name = string.IsNullOrWhiteSpace(postfix)
            ? type.ToString().ToLower()
            : $"{type.ToString().ToLower()}-{postfix.ToLower()}";
        name = BucketInvalidCharactersRegex().Replace(name, "-");
        return name;
    }

    [GeneratedRegex("[^a-z0-9 -]")]
    private static partial Regex BucketInvalidCharactersRegex();
    
    private enum BucketTypeEnum
    {
        Temporary
    }
}
