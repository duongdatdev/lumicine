using LumiCine.Application.Common;

namespace LumiCine.Application.Payments;

public static class VnPayAmount
{
    public const long MaxMinorUnits = 999_999_999_999;

    public static long ToMinorUnits(decimal amount)
    {
        var minor = amount * 100;
        if (minor < 1 || minor > MaxMinorUnits || minor != decimal.Truncate(minor))
            throw new ApiException(400, "The booking total is outside the amount range VNPay accepts.");
        return (long)minor;
    }
}
