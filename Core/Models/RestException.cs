namespace Core.Models
{
    public class RestException : Exception
    {
        public int Code { get; init; }

        public int HttpStatusCode { get; init; }

        public RestException(int code, int httpStatusCode, string? message = null, Exception? innerException = null) : base(message, innerException)
        {
            this.Code = code;
            this.HttpStatusCode = httpStatusCode;
        }
    }
}
