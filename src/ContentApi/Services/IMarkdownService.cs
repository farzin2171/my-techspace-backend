using ContentApi.Models;

namespace ContentApi.Services
{
    public interface IMarkdownService
    {
        // Loads and processes all posts from a source (e.g., local files or GitHub)
        Task<List<Post>> LoadAllPostsAsync();

        // Attempts to get a single post by slug from the cache or loads it
        Task<Post?> GetPostBySlugAsync(string slug);

        // Clears the cache and reloads all content
        Task RefreshContentCacheAsync();
    }
}
