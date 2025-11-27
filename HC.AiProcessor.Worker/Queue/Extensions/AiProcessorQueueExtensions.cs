namespace HC.AiProcessor.Worker.Queue.Extensions;

internal static class AiProcessorQueueExtensions
{
    public static Task WaitForMessageAsync(this IAiProcessorQueue queue, CancellationToken ct)
    {
        if (!queue.IsEmpty)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource();

        queue.MessagesEnqueued += OnMessagesEnqueued;

        if (ct.CanBeCanceled)
        {
            ct.Register(() =>
            {
                queue.MessagesEnqueued -= OnMessagesEnqueued;
                tcs.TrySetCanceled();
            });
        }

        return tcs.Task;

        void OnMessagesEnqueued()
        {
            if (queue.IsEmpty)
                return;

            queue.MessagesEnqueued -= OnMessagesEnqueued;
            tcs.TrySetResult();
        }
    }
}
