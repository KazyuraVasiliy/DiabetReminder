namespace Services.Nightscout.Models
{
    public class Parameters
    {
        public required Glucose Glucose { get; init; }

        public required Delay Delay { get; init; }
    }

    // Установка required привод к ошибке https://github.com/dotnet/runtime/issues/95006
    public class Glucose
    {
        public decimal Hypoglycemia { get; init; }

        public decimal Hyperglycemia { get; init; }

        public decimal Delta { get; init; }

        public decimal HighGlucose { get; init; }

        public decimal LowGlucose { get; init; }
    }

    public class Delay
    {
        public double Error { get; init; }

        public double Warning { get; init; }

        public double Default { get; init; }
    }
}
