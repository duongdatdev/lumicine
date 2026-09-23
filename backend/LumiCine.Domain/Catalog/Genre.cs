using LumiCine.Domain.Common;

namespace LumiCine.Domain.Catalog;

public sealed class Genre
{
    private Genre() => Name = Slug = string.Empty;

    public Genre(Guid id, string name, string slug, DateTime createdAtUtc)
    {
        Id = EntityGuard.Id(id, nameof(id));
        Name = EntityGuard.RequiredText(name?.Trim() ?? string.Empty, 100, nameof(name));
        Slug = NormalizeSlug(slug);
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void Update(string name, string slug)
    {
        Name = EntityGuard.RequiredText(name?.Trim() ?? string.Empty, 100, nameof(name));
        Slug = NormalizeSlug(slug);
    }

    private static string NormalizeSlug(string slug) => EntityGuard.RequiredText(slug?.Trim().ToLowerInvariant() ?? string.Empty, 120, nameof(slug));
}
