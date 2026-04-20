namespace Services.Message
{
    public interface IMessageQueue
    {
        Task EnqueueAsync(Models.Message message, CancellationToken cancellationToken = default);
        IAsyncEnumerable<Models.Message> DequeueAllAsync(CancellationToken cancellationToken = default);
    }
}
