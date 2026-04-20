namespace Core.Services
{
    public interface ITelegramClient
    {
        Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
    }
}
