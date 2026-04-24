using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using MicroseismicSync.Logging;
using MicroseismicSync.Models;

namespace MicroseismicSync.Services
{
    public sealed class WellDataService : IWellDataService
    {
        private const string WellHeaderEndpoint = "dp/api/well_manager/get_well_header_list";
        private const string CreateStyleFileEndpoint = "dp/api/styleconfig/create_style_file";
        private const string GetStyleFileListEndpoint = "dp/api/styleconfig/get_style_file_list";

        private readonly IApiClient apiClient;
        private readonly IAppLogger logger;

        public WellDataService(IApiClient apiClient, IAppLogger logger)
        {
            this.apiClient = apiClient;
            this.logger = logger;
        }

        public async Task<IReadOnlyList<WellInfo>> GetWellsAsync()
        {
            try
            {
                var wellHeaders = await GetWellHeadersAsync().ConfigureAwait(false);
                return wellHeaders
                    .Select(MapWellInfo)
                    .Where(well => !string.IsNullOrWhiteSpace(well.WellName) || !string.IsNullOrWhiteSpace(well.Uwi))
                    .GroupBy(GetWellIdentity, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch (Exception ex)
            {
                logger.Error("Failed to read wells.", ex);
                throw;
            }
        }

        public async Task<IReadOnlyList<StyleFileInfo>> GetStyleFileListAsync(GetStyleFileListRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            try
            {
                var result = await apiClient
                    .PostAsync<GetStyleFileListRequest, List<StyleFileInfo>>(GetStyleFileListEndpoint, request)
                    .ConfigureAwait(false);

                return result ?? new List<StyleFileInfo>();
            }
            catch (Exception ex)
            {
                logger.Error("Failed to get style file list.", ex);
                throw;
            }
        }

        public async Task<bool> CreateStyleFileAsync(CreateStyleFileRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ArgumentException("Style file name is required.", "request");
            }

            if (string.IsNullOrWhiteSpace(request.FilePath))
            {
                throw new ArgumentException("Style file path is required.", "request");
            }

            if (!File.Exists(request.FilePath))
            {
                throw new FileNotFoundException("Style file was not found.", request.FilePath);
            }

            try
            {
                using (var content = BuildStyleFileContent(request))
                {
                    return await apiClient.PostMultipartAsync<bool>(CreateStyleFileEndpoint, content).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.Error("Failed to create style file.", ex);
                throw;
            }
        }

        private async Task<IReadOnlyList<WellHeaderRecord>> GetWellHeadersAsync()
        {
            logger.Debug("Loading well list from " + WellHeaderEndpoint + ".");
            var result = await apiClient.GetAsync<List<WellHeaderRecord>>(WellHeaderEndpoint).ConfigureAwait(false);
            return result ?? new List<WellHeaderRecord>();
        }

        private static MultipartFormDataContent BuildStyleFileContent(CreateStyleFileRequest request)
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent(((int)request.Category).ToString()), "Category");
            content.Add(new StringContent(((int)request.Subcategory).ToString()), "Subcategory");
            content.Add(new StringContent(request.Name), "Name");
            content.Add(new StringContent(((int)request.Scope).ToString()), "Scope");
            content.Add(new StringContent(request.ForceOverwrite ? "true" : "false"), "ForceOverwrite");

            var fileBytes = File.ReadAllBytes(request.FilePath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            content.Add(fileContent, "Content", ResolveStyleFileName(request));
            return content;
        }

        private static string ResolveStyleFileName(CreateStyleFileRequest request)
        {
            return string.IsNullOrWhiteSpace(request.FileName)
                ? Path.GetFileName(request.FilePath)
                : request.FileName;
        }

        private static WellInfo MapWellInfo(WellHeaderRecord source)
        {
            return new WellInfo
            {
                WellId = ParseWellId(source.Id),
                WellName = FirstNonEmpty(source.Name, source.Id),
                WellNumber = source.Id,
                Uwi = source.Id,
                SurfaceX = source.SurfaceX,
                SurfaceY = source.SurfaceY,
                BottomX = source.BottomX,
                BottomY = source.BottomY,
                Kb = source.Kb,
                State = source.ManageDept,
                Region = source.ManageDept,
                County = source.WellDistrict,
                Districts = source.WellDistrict,
            };
        }

        private static string GetWellIdentity(WellInfo well)
        {
            return FirstNonEmpty(well.Uwi, well.WellName, string.Empty);
        }

        private static int ParseWellId(string value)
        {
            int wellId;
            return int.TryParse(value, out wellId) ? wellId : 0;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }

        private sealed class WellHeaderRecord
        {
            public string Id { get; set; }

            public string Name { get; set; }

            public string ManageDept { get; set; }

            public string WellDistrict { get; set; }

            public double? SurfaceX { get; set; }

            public double? SurfaceY { get; set; }

            public double? BottomX { get; set; }

            public double? BottomY { get; set; }

            public double? Kb { get; set; }
        }
    }
}
