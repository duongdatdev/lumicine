using LumiCine.Application.Bookings;
using QRCoder;

namespace LumiCine.Infrastructure.Bookings;

public sealed class TicketQrRenderer : ITicketQrRenderer
{
    private const string PayloadPrefix = "LCT1:";
    private const int PixelsPerModule = 8;

    public byte[] RenderPng(string ticketCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketCode);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(PayloadPrefix + ticketCode, QRCodeGenerator.ECCLevel.Q);
        using var renderer = new PngByteQRCode(data);
        return renderer.GetGraphic(PixelsPerModule);
    }
}
