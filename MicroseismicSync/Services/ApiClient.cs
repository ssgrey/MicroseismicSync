using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using MicroseismicSync.Logging;
using MicroseismicSync.Models;
using MicroseismicSync.Utilities;

namespace MicroseismicSync.Services
{
    public sealed class ApiClient : IApiClient, IDisposable
    {
        private readonly IAppLogger logger;
        private readonly HttpClient client;
        private string baseUrl;
        private bool disposed;

        public ApiClient(IAppLogger logger, bool allowInvalidCertificate, TimeSpan timeout)
        {
            this.logger = logger;

            var handler = new HttpClientHandler();
            if (allowInvalidCertificate)
            {
                handler.ServerCertificateCustomValidationCallback = delegate { return true; };
            }

            client = new HttpClient(handler);
            client.Timeout = timeout;
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public event EventHandler<string> RequestStarted;

        public event EventHandler<string> RequestCompleted;

        public event EventHandler<string> RequestFailed;

        public void SetBaseUrl(string url)
        {
            baseUrl = string.IsNullOrWhiteSpace(url)
                ? string.Empty
                : (url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/");
        }

        public void SetHeaders(string token, string tetProjectId)
        {
            SetHeader("Authorization", token);
            SetHeader("tetproj", tetProjectId);
        }

        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string> parameters = null)
        {
            var url = BuildUrl(endpoint, parameters);
            var operation = "GET " + url;
            var stopwatch = Stopwatch.StartNew();
            OnRequestStarted(operation);

            try
            {
                var response = await client.GetAsync(url).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                stopwatch.Stop();

                logger.Info(string.Format("{0} -> {1} ({2} ms)", operation, response.StatusCode, stopwatch.ElapsedMilliseconds));
                response.EnsureSuccessStatusCode();

                var result = DeserializePayload<T>(content);
                OnRequestCompleted(operation);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.Error(string.Format("{0} failed after {1} ms.", operation, stopwatch.ElapsedMilliseconds), ex);
                OnRequestFailed(operation + " failed.");
                throw;
            }
        }

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            var url = BuildUrl(endpoint, null);
            var operation = "POST " + url;
            var stopwatch = Stopwatch.StartNew();
            OnRequestStarted(operation);

            try
            {
                var json = JsonUtility.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                stopwatch.Stop();

                logger.Info(string.Format("{0} -> {1} ({2} ms)", operation, response.StatusCode, stopwatch.ElapsedMilliseconds));
                response.EnsureSuccessStatusCode();

                var result = DeserializePayload<TResponse>(responseContent);
                OnRequestCompleted(operation);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.Error(string.Format("{0} failed after {1} ms.", operation, stopwatch.ElapsedMilliseconds), ex);
                OnRequestFailed(operation + " failed.");
                throw;
            }
        }

        public async Task<TResponse> PostAsync<TResponse>(string endpoint)
        {
            return await PostAsync<object, TResponse>(endpoint, new object()).ConfigureAwait(false);
        }

        public async Task<TResponse> PostMultipartAsync<TResponse>(string endpoint, MultipartFormDataContent content)
        {
            var url = BuildUrl(endpoint, null);
            var operation = "POST " + url;
            var stopwatch = Stopwatch.StartNew();
            OnRequestStarted(operation);

            try
            {
                var response = await client.PostAsync(url, content).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                stopwatch.Stop();

                logger.Info(string.Format("{0} -> {1} ({2} ms)", operation, response.StatusCode, stopwatch.ElapsedMilliseconds));
                response.EnsureSuccessStatusCode();

                var result = DeserializePayload<TResponse>(responseContent);
                OnRequestCompleted(operation);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.Error(string.Format("{0} failed after {1} ms.", operation, stopwatch.ElapsedMilliseconds), ex);
                OnRequestFailed(operation + " failed.");
                throw;
            }
        }

        public string BuildUrl(string endpoint, Dictionary<string, string> parameters = null)
        {
            string url;
            if (IsAbsoluteUrl(endpoint))
            {
                url = endpoint;
            }
            else
            {
                var normalizedBaseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
                var normalizedEndpoint = (endpoint ?? string.Empty).TrimStart('/');
                url = string.IsNullOrWhiteSpace(normalizedBaseUrl)
                    ? normalizedEndpoint
                    : normalizedBaseUrl + "/" + normalizedEndpoint;
            }

            if (parameters != null && parameters.Count > 0)
            {
                var queryString = string.Join(
                    "&",
                    parameters
                        .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Key))
                        .Select(parameter => Uri.EscapeDataString(parameter.Key) + "=" + Uri.EscapeDataString(parameter.Value ?? string.Empty)));

                if (!string.IsNullOrWhiteSpace(queryString))
                {
                    url += "?" + queryString;
                }
            }

            return url;
        }

        private static bool IsAbsoluteUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private T DeserializePayload<T>(string content)
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)content;
            }

            if (LooksLikeEnvelope(content))
            {
                var response = JsonUtility.Deserialize<ApiResponse<T>>(content);
                if (!response.Success)
                {
                    throw new InvalidOperationException(response.Message ?? "API request failed.");
                }

                return response.Data;
            }

            return JsonUtility.Deserialize<T>(content);
        }

        private static bool LooksLikeEnvelope(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            return content.IndexOf("\"data\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   content.IndexOf("\"success\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetHeader(string headerName, string value)
        {
            if (client.DefaultRequestHeaders.Contains(headerName))
            {
                client.DefaultRequestHeaders.Remove(headerName);
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                client.DefaultRequestHeaders.Add(headerName, value);
            }
        }

        private void OnRequestStarted(string operation)
        {
            var handler = RequestStarted;
            if (handler != null)
            {
                handler(this, operation);
            }
        }

        private void OnRequestCompleted(string operation)
        {
            var handler = RequestCompleted;
            if (handler != null)
            {
                handler(this, operation);
            }
        }

        private void OnRequestFailed(string operation)
        {
            var handler = RequestFailed;
            if (handler != null)
            {
                handler(this, operation);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            client.Dispose();
            disposed = true;
        }
    }
}
