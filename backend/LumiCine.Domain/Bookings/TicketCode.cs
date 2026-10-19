using System.Buffers.Text;
using System.Security.Cryptography;

namespace LumiCine.Domain.Bookings;

public static class TicketCode
{
    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
