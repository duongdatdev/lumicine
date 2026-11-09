using System.Globalization;
using LumiCine.Application.Common;
using LumiCine.Application.Reporting;

namespace LumiCine.API.Reporting;

internal static class ReportDateQueryValidation
{
    public static void Validate(HttpRequest request, ReportFilter filter)
    {
        ValidateDate(request, "fromDate", filter.FromDate);
        ValidateDate(request, "toDate", filter.ToDate);
    }

    private static void ValidateDate(HttpRequest request, string key, DateOnly? bound)
    {
        var names = new[] { key, $"filter.{key}" };
        var matchingNames = names.Where(request.Query.ContainsKey).ToArray();
        if (matchingNames.Length == 0) return;
        if (matchingNames.Length != 1) throw new ApiException(400, $"{key} must be provided once in yyyy-MM-dd format.");
        var raw = request.Query[matchingNames[0]];
        if (raw.Count != 1 || !DateOnly.TryParseExact(raw[0], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) || parsed != bound)
            throw new ApiException(400, $"{key} must use yyyy-MM-dd format.");
    }
}
