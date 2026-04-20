namespace Services.Message.Models
{
    [Flags]
    public enum Channels
    {
        None = 0,
        Telegram = 1,
        Email = 2
    }
}
