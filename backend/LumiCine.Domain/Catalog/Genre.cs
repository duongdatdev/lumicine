using LumiCine.Domain.Common;

namespace LumiCine.Domain.Catalog;

public sealed class Genre
{
    private Genre() => Name = Slug = string.Empty;

    public Genre(Guid id, string name, string slug, DateTime createdAtUtc)
    {
        Id = EntityGuard.Id(id, nameof(id));
        Name = EntityGuard.RequiredText(name, 100, nameof(name));
        Slug = EntityGuard.RequiredText(slug, 120, nameof(slug));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}