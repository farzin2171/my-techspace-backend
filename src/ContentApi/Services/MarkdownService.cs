using ContentApi.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Microsoft.Extensions.Caching.Memory;
using YamlDotNet.Serialization;

namespace ContentApi.Services
{
    public class MarkdownService : IMarkdownService
    {
        private readonly IMemoryCache _cache;
        private readonly MarkdownPipeline _pipeline;
        private readonly IDeserializer _yamlDeserializer;
        private const string CacheKeyAllPosts = "AllPostsCacheKey";
        private const string ContentDirectory = "Content"; // Local folder for .md files

        public MarkdownService(IMemoryCache cache)
        {
            _cache = cache;

            // Markdig configuration for Frontmatter and GFM extensions
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseYamlFrontMatter()
                .Build();

            // YamlDotNet configuration for deserializing the Frontmatter
            _yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
                .Build();
        }

        public async Task<List<Post>> LoadAllPostsAsync()
        {
            // Check cache first
            if (_cache.TryGetValue(CacheKeyAllPosts, out List<Post>? cachedPosts) && cachedPosts != null)
            {
                return cachedPosts;
            }






            // Cache miss: Load from source (local files in this example)
            var posts = new List<Post>();
            var contentPath = Path.Combine(Directory.GetCurrentDirectory(), ContentDirectory);
            if (!Directory.Exists(contentPath)) return posts;

            // Get all .md files (simulate fetching from GitHub)
            var files = Directory.GetFiles(contentPath, "*.md");

            foreach (var filePath in files)
            {
                var rawContent = await File.ReadAllTextAsync(filePath);
                var post = ProcessMarkdown(rawContent);
                posts.Add(post);
            }

            // Order by date (newest first)
            posts = posts.OrderByDescending(p => p.Date).ToList();

            // Store in cache
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(10)); // Cache for 10 minutes

            _cache.Set(CacheKeyAllPosts, posts, cacheOptions);

            return posts;
        }

        public async Task<Post?> GetPostBySlugAsync(string slug)
        {
            // Load all posts (which hits the cache if available)
            var allPosts = await LoadAllPostsAsync();

            // Find the specific post
            return allPosts.FirstOrDefault(p => p.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        }

        public Task RefreshContentCacheAsync()
        {
            // Manual invalidation
            _cache.Remove(CacheKeyAllPosts);
            return Task.CompletedTask;
        }

        private Post ProcessMarkdown(string rawContent)
        {
            var document = Markdown.Parse(rawContent, _pipeline);
            var metadata = new PostMetadata();
            string postContent = rawContent;

            // Extract Frontmatter
            var frontMatterBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
            if (frontMatterBlock != null)
            {
                var yaml = frontMatterBlock.Lines.ToString();
                metadata = _yamlDeserializer.Deserialize<PostMetadata>(yaml);

                // Remove the frontmatter block from the content for clean rendering
                var start = rawContent.IndexOf("---", 3) + 3;
                postContent = rawContent.Substring(start).TrimStart('\r', '\n');
            }

            // Convert remaining markdown to HTML
            var htmlContent = Markdown.ToHtml(postContent, _pipeline);

            return new Post
            {
                Title = metadata.Title,
                Slug = metadata.Slug,
                Date = metadata.Date,
                Tags = metadata.Tags,
                Summary = metadata.Summary,
                HtmlContent = htmlContent
            };
        }
    }
}