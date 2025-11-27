using System.Security.Cryptography;
using System.Text;
using HC.AiProcessor.Entity.Ai;
using HC.AiProcessor.Infrastructure.Repositories.Ai;
using HC.Packages.Persistent.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;

namespace HC.AiProcessor.Application.Services;

public interface IAiEmbeddingCache
{
    Task<Vector?> GetValueAsync(string text, CancellationToken ct = default);
    Task SetValueAsync(string text, Vector value, CancellationToken ct = default);
    Task<bool> IsExistAsync(string text, CancellationToken ct = default);
    Task DeleteAsync(string text, CancellationToken ct = default);
}

internal sealed class AiEmbeddingCache(IServiceProvider serviceProvider) : IAiEmbeddingCache
{
    private const int PreviewMaxLength = 32;

    private readonly IServiceProvider _serviceProvider =
        serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public async Task<Vector?> GetValueAsync(string text, CancellationToken ct = default)
    {
        return await DoScopedWork(async (repository, _) =>
        {
            string hash = GenerateHash(text);
            return await repository.GetValueOrNullByHash(hash, ct);
        });
    }

    public async Task SetValueAsync(string text, Vector value, CancellationToken ct = default)
    {
        await DoScopedWork(async (repository, uow) =>
        {
            string hash = GenerateHash(text);

            AiEmbedding? entity = await repository.GetOrNullByHash(hash, ct);

            if (entity is null)
            {
                entity = new AiEmbedding
                {
                    Hash = hash,
                    Preview = GeneratePreview(text),
                    Value = value
                };

                await repository.Create(entity, ct);
            }
            else
            {
                entity.Value = value;

                await repository.Update(entity, ct);
            }

            await uow.SaveChangesAsync(ct);
        });
    }

    public async Task<bool> IsExistAsync(string text, CancellationToken ct = default)
    {
        return await DoScopedWork(async (repository, _) =>
        {
            string hash = GenerateHash(text);
            return await repository.IsExist(hash, ct);
        });
    }

    public async Task DeleteAsync(string text, CancellationToken ct = default)
    {
        await DoScopedWork(async (repository, uow) =>
        {
            string hash = GenerateHash(text);

            AiEmbedding? entity = await repository.GetOrNullByHash(hash, ct);
            if (entity is null)
                return;

            await repository.Delete(entity, ct);
            await uow.SaveChangesAsync(ct);
        });
    }

    private async Task DoScopedWork(Func<IAiEmbeddingRepository, IUnitOfWork, Task> action)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IAiEmbeddingRepository>();

        await action(repository, uow);
    }

    private async Task<T> DoScopedWork<T>(Func<IAiEmbeddingRepository, IUnitOfWork, Task<T>> action)
    {
        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = scope.ServiceProvider.GetRequiredService<IAiEmbeddingRepository>();

        T result = await action(repository, uow);
        return result;
    }

    private static string GenerateHash(string input)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        string result = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        return result;
    }

    private static string GeneratePreview(string input)
    {
        if (input.Length > PreviewMaxLength)
            input = input[..PreviewMaxLength];

        return input;
    }
}
