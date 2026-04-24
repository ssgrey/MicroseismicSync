namespace MicroseismicSync.Models
{
    public sealed class ApiLaunchContext
    {
        public string RawArgument { get; set; }

        public string BaseUrl { get; set; }

        public string Token { get; set; }

        public string TetProjectId { get; set; }

        public string Ip { get; set; }

        public string Port { get; set; }

        public string ProjectName { get; set; }

        public string CaseId { get; set; }

        public int Type { get; set; }
    }
}
