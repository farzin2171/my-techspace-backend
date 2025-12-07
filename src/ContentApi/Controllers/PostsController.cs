using ContentApi.Models;
using ContentApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PostsController : ControllerBase
    {
        private readonly IMarkdownService _markdownService;

        public PostsController(IMarkdownService markdownService)
        {
            _markdownService = markdownService;
        }

        /// <summary>
        /// Retrieves a list of all posts with summary data (no full HTML content).
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<PostSummary>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPosts()
        {
            var posts = await _markdownService.LoadAllPostsAsync();

            // Map the full Post model to the lightweight PostSummary model
            var summaries = posts.Select(p => new PostSummary
            {
                Title = p.Title,
                Slug = p.Slug,
                Date = p.Date,
                Tags = p.Tags,
                Summary = p.Summary,
                ReadTimeMinutes = p.ReadTimeMinutes

            });

            return Ok(summaries);
        }

        /// <summary>
        /// Retrieves the full post content by slug, including the HTML content.
        /// </summary>
        [HttpGet("{slug}")]
        [ProducesResponseType(typeof(Post), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPostBySlug(string slug)
        {
            var post = await _markdownService.GetPostBySlugAsync(slug);

            if (post == null)
            {
                return NotFound();
            }

            return Ok(post);
        }

        /// <summary>
        /// Forces the API to clear the cache and reload all content from source.
        /// NOTE: Secure this endpoint in a production environment!
        /// </summary>
        [HttpPost("refresh")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RefreshCache()
        {
            await _markdownService.RefreshContentCacheAsync();
            // Optionally, force a reload immediately after clearing
            await _markdownService.LoadAllPostsAsync();
            return Ok(new { message = "Content cache refreshed and reloaded." });
        }
    }
}