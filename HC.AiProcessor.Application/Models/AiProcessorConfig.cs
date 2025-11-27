namespace HC.AiProcessor.Application.Models;

public class AiProcessorConfig
{
    public int AiSettingsTtlInSeconds { get; set; } = 3600;
    public int OpenAiClientTimeoutInSeconds { get; set; } = 160;
}
