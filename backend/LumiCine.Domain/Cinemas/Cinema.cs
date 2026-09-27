using LumiCine.Domain.Common;

namespace LumiCine.Domain.Cinemas;

public sealed class Cinema
{
    private Cinema() => Code = Name = Address = City = string.Empty;

    public Cinema(
        Guid id,
        string code,
        string name,
        string address,
        string city,
        DateTime createdAtUtc,
        bool isActive = true,
        DateTime? updatedAtUtc = null)
    {
        Id = EntityGuard.Id(id, nameof(id));
        Code = EntityGuard.RequiredText(code.Trim().ToUpperInvariant(), 30, nameof(code));
        Name = EntityGuard.RequiredText(name.Trim(), 160, nameof(name));
        Address = EntityGuard.RequiredText(address.Trim(), 300, nameof(address));
        City = EntityGuard.RequiredText(city.Trim(), 100, nameof(city));
        CreatedAtUtc = EntityGuard.Utc(createdAtUtc, nameof(createdAtUtc));
        IsActive = isActive;
        UpdatedAtUtc = EntityGuard.OptionalUtc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string Address { get; private set; }
    public string City { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public void Update(string code, string name, string address, string city, DateTime updatedAtUtc)
    {
        Code = EntityGuard.RequiredText(code.Trim().ToUpperInvariant(), 30, nameof(code));
        Name = EntityGuard.RequiredText(name.Trim(), 160, nameof(name));
        Address = EntityGuard.RequiredText(address.Trim(), 300, nameof(address));
        City = EntityGuard.RequiredText(city.Trim(), 100, nameof(city));
        UpdatedAtUtc = EntityGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    public void SetActive(bool isActive, DateTime updatedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = EntityGuard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }
}
