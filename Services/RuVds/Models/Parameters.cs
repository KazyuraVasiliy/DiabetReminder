namespace Services.RuVds.Models
{
    public class Parameters
    {
        public required int ServerId { get; init; }

        public required Delay Delay { get; init; }
    }

    public class Delay
    {
        public double Paid { get; init; }

        public double Status { get; init; }
    }
}
