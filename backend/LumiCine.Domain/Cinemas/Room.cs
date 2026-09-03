using LumiCine.Domain.Common;

namespace LumiCine.Domain.Cinemas;

public sealed class Room
{
    private Room() => Code = Name = string.Empty;

    public Room(
        Guid id,
        Guid cinemaId,
        string code,
        string name,
        DateTime createdAtUtc,
        bool isActive = true,
        DateTime? updatedAtUtc = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        CinemaId = EntityGuard.Id(cinemaId, nameof(cinemaId));
        Code = EntityGuard.RequiredText(code, 30, nameof(code));
        Name = EntityGuard.RequiredText(name, 100, nameof(name));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        IsActive = isActive;
        UpdatedAtUtc = EntityGuard.OptionalUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public Guid Id { get; private set; }
    public Guid CinemaId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
}