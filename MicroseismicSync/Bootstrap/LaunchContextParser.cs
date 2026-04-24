using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Script.Serialization;
using MicroseismicSync.Models;

namespace MicroseismicSync.Bootstrap
{
    public sealed class LaunchContextParser
    {
        public ApiLaunchContext Parse(string[] args)
        {
            var rawArgument = args == null
                ? string.Empty
                : args.FirstOrDefault(argument => !string.IsNullOrWhiteSpace(argument)) ?? string.Empty;

            if (args == null ||  args.Length == 0)
            {
                rawArgument = System.IO.File.ReadAllText("LaunchArgs.txt");
            }
             var configuredBootstrapUri = ConfigurationManager.AppSettings["BootstrapUri"];
            var source = !string.IsNullOrWhiteSpace(rawArgument) ? rawArgument : configuredBootstrapUri;

            var launchContext = TryParseProtocolPayload(source) ?? new ApiLaunchContext();
            launchContext.RawArgument = source ?? string.Empty;

            if (string.IsNullOrWhiteSpace(launchContext.BaseUrl))
            {
                launchContext.BaseUrl = ConfigurationManager.AppSettings["ApiBaseUrl"];
            }

            if (string.IsNullOrWhiteSpace(launchContext.Token))
            {
                launchContext.Token = ConfigurationManager.AppSettings["ApiToken"];
            }

            if (string.IsNullOrWhiteSpace(launchContext.TetProjectId))
            {
                launchContext.TetProjectId = ConfigurationManager.AppSettings["ApiProjectId"];
            }

            if (string.IsNullOrWhiteSpace(launchContext.ProjectName))
            {
                launchContext.ProjectName = ConfigurationManager.AppSettings["ApiProjectName"];
            }

            if (string.IsNullOrWhiteSpace(launchContext.BaseUrl) && !string.IsNullOrWhiteSpace(launchContext.Ip))
            {
                launchContext.BaseUrl = BuildBaseUrl(launchContext.Ip, launchContext.Port);
            }

            launchContext.BaseUrl = NormalizeBaseUrl(launchContext.BaseUrl);
            return launchContext;
        }

        private static ApiLaunchContext TryParseProtocolPayload(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            var jsonStartIndex = source.IndexOf('{');
            var jsonEndIndex = source.LastIndexOf('}');

            if (jsonStartIndex < 0 || jsonEndIndex <= jsonStartIndex)
            {
                return null;
            }

            var json = source.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex + 1);
            var serializer = new JavaScriptSerializer();
            var dictionary = serializer.Deserialize<Dictionary<string, object>>(json);

            var launchContext = new ApiLaunchContext
            {
                Token = ReadString(dictionary, "token"),
                TetProjectId = ReadString(dictionary, "tetproj"),
                Ip = ReadString(dictionary, "ip"),
                Port = ReadString(dictionary, "port"),
                ProjectName = ReadString(dictionary, "projectname"),
                CaseId = ReadString(dictionary, "caseId"),
            };

            int type;
            if (int.TryParse(ReadString(dictionary, "type"), out type))
            {
                launchContext.Type = type;
            }

            return launchContext;
        }

        private static string BuildBaseUrl(string ip, string port)
        {
            var scheme = string.Equals(port, "30015", StringComparison.OrdinalIgnoreCase)
                ? "http"
                : "https";

            if (string.IsNullOrWhiteSpace(port))
            {
                return string.Format("{0}://{1}/tet/", scheme, ip);
            }

            return string.Format("{0}://{1}:{2}/tet/", scheme, ip, port);
        }

        private static string NormalizeBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            return baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/";
        }

        private static string ReadString(IDictionary<string, object> dictionary, string key)
        {
            object value;
            return dictionary.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : string.Empty;
        }
    }
}
