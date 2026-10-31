using System.Globalization;
using System.Text;
using LumiCine.Application.Discovery;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Deterministic Vietnamese fallback answers built purely from server-side evidence. They never
/// invent values: times, prices and seat counts are formatted from the evidence records only.
/// </summary>
public static class DiscoveryAnswerTemplates
{
    public static string BuildResultsAnswer(IReadOnlyList<DiscoveryEvidenceMovie> movies, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        var number = 1;
        foreach (var movie in movies)
        {
            var genres = movie.Genres.Count > 0 ? $", thể loại: {string.Join(", ", movie.Genres)}" : string.Empty;
            sb.AppendLine($"{number}. {movie.Title} ({movie.DurationMinutes} phút{genres})");
            foreach (var showtime in movie.Showtimes)
            {
                var local = showtime.StartAtUtc + DiscoveryTime.ZoneOffset;
                sb.AppendLine($"{local:HH:mm} ngày {local:dd/MM} tại {showtime.CinemaName} ({showtime.RoomName}), giá từ {FormatVnd(showtime.Price)}, còn {showtime.AvailableSeats} ghế.");
            }
            number++;
        }
        return sb.ToString().TrimEnd();
    }

    public static string BuildNoResultsAnswer(DiscoveryFiltersView filters, DateTime nowUtc)
    {
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.TopicText))
            conditions.Add($"chủ đề \"{filters.TopicText}\"");
        if (!string.IsNullOrWhiteSpace(filters.MovieNameText))
            conditions.Add($"phim \"{filters.MovieNameText}\"");
        if (filters.GenreSlugs.Count > 0)
            conditions.Add($"thể loại {string.Join(", ", filters.GenreSlugs)}");
        if (filters.MaxDurationMinutes is { } duration)
            conditions.Add($"thời lượng dưới {duration} phút");
        if (!string.IsNullOrWhiteSpace(filters.City))
            conditions.Add($"thành phố {filters.City}");
        if (!string.IsNullOrWhiteSpace(filters.CinemaName))
            conditions.Add($"rạp {filters.CinemaName}");
        if (filters.DateFrom is not null || filters.DateTo is not null)
        {
            var from = filters.DateFrom ?? filters.DateTo ?? DiscoveryTime.TodayInZone(nowUtc);
            var to = filters.DateTo ?? filters.DateFrom ?? from;
            conditions.Add($"ngày {from:dd/MM}–{to:dd/MM}");
        }
        if (filters.TimeWindowStartMinutes is { } start && filters.TimeWindowEndMinutes is { } end)
            conditions.Add($"khung giờ {FormatClock(start)}–{FormatClock(end)}");
        else if (filters.TimeWindowStartMinutes is { } onlyStart)
            conditions.Add($"sau {FormatClock(onlyStart)}");
        else if (filters.TimeWindowEndMinutes is { } onlyEnd)
            conditions.Add($"trước {FormatClock(onlyEnd)}");
        if (filters.MaxPrice is { } maxPrice)
            conditions.Add($"giá tối đa {FormatVnd(maxPrice)}");
        if (filters.MinSeats is { } minSeats)
            conditions.Add($"ít nhất {minSeats} ghế trống");

        return conditions.Count == 0
            ? "Mình chưa tìm thấy phim nào khớp với yêu cầu của bạn. Bạn thử nói thể loại hoặc phim bạn muốn xem nhé."
            : $"Mình chưa tìm thấy phim nào khớp với điều kiện bạn đưa: {string.Join(", ", conditions)}. Bạn thử nới thời gian, đổi rạp hoặc bỏ một điều kiện nhé.";
    }

    /// <summary>Notice shown when the AI writer is unavailable and a template answer is used instead.</summary>
    public static string BuildDegradedNotice() =>
        "Dịch vụ AI tạm thời không trả lời được, mình dùng câu trả lời mẫu từ dữ liệu tìm kiếm.";

    internal static string FormatClock(int minutes) => $"{minutes / 60:D2}:{minutes % 60:D2}";

    // Invariant "N0" groups with commas; VND convention writes dot separators.
    internal static string FormatVnd(decimal price) =>
        ((long)decimal.Truncate(price)).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.') + "đ";
}
