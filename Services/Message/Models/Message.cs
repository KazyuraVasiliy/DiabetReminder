namespace Services.Message.Models
{
    public class Message
    {
        public string? Subject { get; set; }

        public string Body { get; set; } = 
            string.Empty;

        public Types? Type { get; set; }

        public Channels? Channel { get; set; }

        public string[]? Users { get; set; }
    }
}
