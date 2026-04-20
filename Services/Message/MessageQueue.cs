using System.Threading.Channels;

namespace Services.Message
{
    public class MessageQueue : IMessageQueue
    {
        private readonly Channel<Models.Message> _channel;

        public MessageQueue()
        {
            var options = new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait
            };

            _channel = Channel.CreateBounded<Models.Message>(options);
        }

        public async Task EnqueueAsync(Models.Message message, CancellationToken cancellationToken = default) =>
            await _channel.Writer.WriteAsync(message, cancellationToken);

        public IAsyncEnumerable<Models.Message> DequeueAllAsync(CancellationToken cancellationToken = default) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
