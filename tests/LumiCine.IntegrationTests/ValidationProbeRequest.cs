using System.ComponentModel.DataAnnotations;

namespace LumiCine.IntegrationTests;

public sealed class ValidationProbeRequest
{
    [Required]
    public string? Name { get; set; }

    [Range(1, 10)]
    public int Quantity { get; set; }
}