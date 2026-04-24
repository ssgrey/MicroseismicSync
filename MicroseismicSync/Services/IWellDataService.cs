using System.Collections.Generic;
using System.Threading.Tasks;
using MicroseismicSync.Models;

namespace MicroseismicSync.Services
{
    public interface IWellDataService
    {
        Task<IReadOnlyList<WellInfo>> GetWellsAsync();

        Task<IReadOnlyList<StyleFileInfo>> GetStyleFileListAsync(GetStyleFileListRequest request);

        Task<bool> CreateStyleFileAsync(CreateStyleFileRequest request);
    }
}
