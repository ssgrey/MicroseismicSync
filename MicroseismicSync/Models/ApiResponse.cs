namespace MicroseismicSync.Models
{
    public sealed class ApiResponse<T>
    {
        public bool Success { get; set; } = true;

        public string Message { get; set; }

        public int Code { get; set; }

        public T Data { get; set; }
    }
}
