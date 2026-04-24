using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace MicroseismicSync.Services
{
    public interface IApiClient
    {
        event EventHandler<string> RequestStarted;

        event EventHandler<string> RequestCompleted;

        event EventHandler<string> RequestFailed;

        void SetBaseUrl(string url);

        void SetHeaders(string token, string tetProjectId);

        Task<T> GetAsync<T>(string endpoint, Dictionary<string, string> parameters = null);

        Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest data);

        Task<TResponse> PostAsync<TResponse>(string endpoint);

        Task<TResponse> PostMultipartAsync<TResponse>(string endpoint, MultipartFormDataContent content);

        string BuildUrl(string endpoint, Dictionary<string, string> parameters = null);
    }
}
