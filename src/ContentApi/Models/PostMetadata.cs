using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace ContentApi.Models;

public class PostMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    // YamlDotNet requires this for property names that don't match C# conventions
    [YamlMember(Alias = "date")]
    public DateTime Date { get; set; }

    public List<string> Tags { get; set; } = new List<string>();
    public string Summary { get; set; } = string.Empty;

    public int ReadTimeMinutes { get; set; }
}
