namespace HC.AiProcessor.Application.Services;

public interface IRandomIdGenerator
{
    string GenerateId();
}

internal sealed class RandomIdGenerator : IRandomIdGenerator
{
    public string GenerateId() => Guid.NewGuid().ToString();
}
