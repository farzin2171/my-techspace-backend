namespace ContentApi.Models;

public class Post
{
    // Metadata (from Frontmatter)
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
    public string Summary { get; set; } = string.Empty;

    // The processed content
    public string HtmlContent { get; set; } = string.Empty;
}
