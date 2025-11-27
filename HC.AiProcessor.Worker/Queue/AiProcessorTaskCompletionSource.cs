namespace HC.AiProcessor.Worker.Queue;

internal interface IAiProcessorTaskCompletionSource
{
    void SetResult(object? result);
}

internal interface IAiProcessorTaskResultProvider
{
    object? Result { get; }
}

internal sealed class AiProcessorTaskCompletionSource :
    IAiProcessorTaskCompletionSource,
    IAiProcessorTaskResultProvider
{
    public void SetResult(object? result)
    {
        Result = result;
    }

    public object? Result { get; set; }
}
