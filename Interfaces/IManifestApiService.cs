using SolusManifestApp.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SolusManifestApp.Interfaces
{
    public interface IManifestApiService
    {
        Task<Manifest?> GetManifestAsync(string appId, string apiKey);
        Task<List<Manifest>?> SearchGamesAsync(string query, string apiKey);
        Task<List<Manifest>?> GetAllGamesAsync(string apiKey);
        bool ValidateApiKey(string apiKey);
        Task<bool> TestApiKeyAsync(string apiKey);
        Task<GameStatus?> GetGameStatusAsync(string appId, string apiKey);
        Task<LibraryResponse?> GetLibraryAsync(string apiKey, int limit = 100, int offset = 0, string? search = null, string sortBy = "updated");
        Task<SearchResponse?> SearchLibraryAsync(string query, string apiKey, int limit = 50, bool searchByAppId = false);
    }
}
