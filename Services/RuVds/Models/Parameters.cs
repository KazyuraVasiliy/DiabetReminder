namespace Services.RuVds.Models
{
    public class Parameters
    {
        public required int ServerId { get; init; }

        public required Delay Delay { get; init; }

        public required Users? Users { get; init; }

        public required Channels? Channels { get; init; }
    }

    public class Delay
    {
        public double Paid { get; init; }

        public double Status { get; init; }
    }

    public class Users
    {
        public string[]? Error { get; init; }

        public string[]? Paid { get; init; }

        public string[]? Status { get; init; }
    }

    public class Channels
    {
        public string[]? Error { get; init; }

        public string[]? Paid { get; init; }

        public string[]? Status { get; init; }
    }
}
