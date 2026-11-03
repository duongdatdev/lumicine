using System.Globalization;
using LumiCine.Application.Discovery;

namespace LumiCine.IntegrationTests.Discovery;

/// <summary>
/// Static evaluation dataset for Phase 6 discovery quality. This file holds no database, no test
/// collection and no runtime harness: it is pure graded data plus data-integrity tests. A later
/// harness replays each <see cref="DiscoveryEvalScenario"/> against the runtime and compares the
/// result with <see cref="DiscoveryEvalScenario.Expect"/>.
/// </summary>

/// <summary>One fictional movie of the eval catalog; <see cref="GenreSlugs"/> reuse the product's dash-case slugs.</summary>
internal sealed record DiscoveryEvalMovie(
    string Key,
    string Title,
    string? OriginalTitle,
    int DurationMinutes,
    string? AgeRating,
    string[] GenreSlugs,
    string Synopsis);

/// <summary>Fictional cinema; names reuse the vocabulary of the other discovery tests.</summary>
internal sealed record DiscoveryEvalCinema(string Name, string City);

/// <summary>Fictional but Vietnam-realistic catalog backing the semantic scenarios.</summary>
internal static class DiscoveryEvalCatalog
{
    public static readonly DiscoveryEvalMovie[] Movies =
    [
        new(
            "vong-xoay-thoi-gian",
            "Vòng Xoáy Thời Gian",
            "The Time Spiral",
            118,
            "T16",
            ["khoa-hoc-truyen-vien", "chinh-kich"],
            "Nhà vật lý trẻ Minh chế tạo thành công cỗ máy du hành thời gian và vô tình quay ngược về quá khứ mười năm trước. Mỗi vòng xoáy thời gian đưa anh đến một lựa chọn: cứu người anh yêu hay giữ nguyên dòng chảy của lịch sử. Khi ký ức và thực tại xô đẩy nhau, anh nhận ra có những điều trong quá khứ không được phép chạm vào."),
        new(
            "mua-he-cua-be-thu",
            "Mùa Hè Của Bé Thư",
            null,
            102,
            "P",
            ["gia-dinh", "chinh-kich"],
            "Bé Thư mười tuổi được gửi về quê để trải qua một mùa hè cùng ông ngoại nghiêm khắc. Qua những ngày bắt cua, thả diều và nghe kể chuyện thời xưa, hai ông cháu dần tìm lại sự kết nối máu mủ. Một câu chuyện gia đình ấm áp về tình thân, tuổi thơ và sự bao dung."),
        new(
            "gau-bong-phieu-luu-ky",
            "Gấu Bông Phiêu Lưu Ký",
            "The Teddy Odyssey",
            88,
            "P",
            ["hoat-hinh", "gia-dinh"],
            "Chú gấu bông Bông Bông bị đánh rơi trên chuyến xe khách và bắt đầu cuộc phiêu lưu băng qua thành phố để tìm về cô chủ bé nhỏ. Trên đường đi, chú kết bạn với chị mèo hoang lười biếng và anh chim sẻ hay nói. Bộ hoạt hình tươi sáng, tình cảm, dành cho trẻ em và cả gia đình xem cùng nhau."),
        new(
            "truy-tim-bong-dem",
            "Truy Tìm Bóng Đêm",
            "Chasing the Night",
            126,
            "T16",
            ["hanh-dong", "chinh-kich"],
            "Cựu trinh sát Khôi quay lại thành phố để truy tìm tổ chức buôn người ẩn mình sau những quán bar về đêm. Các pha rượt đuổi tốc độ cao, đấu súng trong hầm xe và trận tay đôi cuối cùng diễn ra dồn dập không kịp thở. Phim hành động giật gân với nút thắt phản bội mà khán giả không ngờ tới."),
        new(
            "loi-the-quy-an",
            "Lời Thề Quỷ Án",
            null,
            97,
            "T18",
            ["kinh-di"],
            "Sau đám cưới của cặp đôi trẻ, một lời thề cổ trong ngôi đền đổ nát bỗng được đánh thức. Đêm nào làng cũng nghe tiếng gõ cửa ba nhịp và thấy bóng người tóc dài đứng dưới gốc đa cũ. Phim kinh dị ma quái với nhiều pha giật mình và không khí rùng rợn đến giây cuối cùng."),
        new(
            "cuoi-kip-chay",
            "Cưới Kịp Chạy",
            "Run to the Wedding",
            95,
            "T13",
            ["hai", "lang-man"],
            "Chỉ còn bốn ngày nữa là đến đám cưới, chú rể Nhân tỉnh dậy và nhận ra mình đã quên mất tiệc cưới đặt ở đâu. Cùng cô bạn thân lắm chuyện, anh chạy vòng vòng khắp thành phố để cầu xin sự tha thứ của cô dâu. Hài hước nhẹ nhàng với loạt tình huống dở khóc dở cười."),
        new(
            "ha-noi-mua-dong-do",
            "Hà Nội Mùa Đông Đó",
            null,
            112,
            "T16",
            ["lang-man", "chinh-kich"],
            "Hà Nội mùa đông, cô họa sĩ trẻ tình cờ gặp lại người yêu cũ sau mười năm im lặng trong một quán cà phê nhỏ. Bức thư chưa gửi và những cơn mưa phùn đầu mùa giữ cả hai ở lại giữa quá khứ và hiện tại. Tình cảm lãng mạn, man mác như hương hoa sữa cuối phố."),
        new(
            "rung-truong-son",
            "Rừng Trường Sơn",
            "The Hidden Wild",
            85,
            "P",
            ["tai-lieu"],
            "Bộ phim tài liệu ghi hình suốt ba năm dọc dãy Trường Sơn, theo chân đoàn khảo sát rừng núi miền Tây. Hổ Đông Dương, sao la và hàng trăm loài quý hiếm xuất hiện qua những bẫy ảnh lần đầu được kích hoạt. Thiên nhiên hoang sơ lẫn cảnh rừng bị chặt phá là tiếng chuông cảnh tỉnh cho bảo tồn."),
        new(
            "duong-len-dinh-quan-1968",
            "Đường Lên Định Quan 1968",
            null,
            165,
            "T16",
            ["chien-tranh", "chinh-kich"],
            "Mùa hè 1968, một tiểu đội thanh niên xung phong nhận nhiệm vụ mở lại con đường chi viện cho mặt trận Định Quan. Giữa mưa bom bão đạn, họ vừa gỡ bom từ từ vừa chuyển từng bó thư từ, thuốc men lên chiến khu. Phim chiến tranh lịch sử tái hiện sự hy sinh thầm lặng và lòng yêu nước của thế hệ cha anh."),
        new(
            "nhip-trong-sai-gon",
            "Nhịp Trống Sài Gòn",
            null,
            108,
            "P",
            ["am-nhac", "gia-dinh"],
            "Cậu thanh niên miền Nam say mê tiếng trống cổ phong nhưng bị cha bắt nối nghiệp gia truyền. Từ hẻm nhỏ đến bến cầu, cậu gõ trống khắp Sài Gòn để giành một suất trong đại nhạc hội cuối năm. Phim âm nhạc truyền cảm hứng về ước mơ, gia đình và nhịp sống Sài Gòn."),
        new(
            "ho-so-vu-an-so-7",
            "Hồ Sơ Vụ Án Số 7",
            "Case File No. 7",
            141,
            "T16",
            ["chinh-kich", "hanh-dong"],
            "Thiếu tá đội điều tra Nội nhận bộ hồ sơ vụ án số 7 về một cái chết đã được kết luận là tai nạn lao động. Từng bản ghi hình bị xoá, từng nhân chứng đổi lời khai kéo cô vào vòng vây của những kẻ muốn chôn vùi sự thật. Phim hình sự kịch tính, điều tra dồn dập xen vài pha hành động chớp nhoáng."),
        new(
            "san-co-khong-cau-thu",
            "Sân Cỏ Không Cầu Thủ",
            "The Empty Pitch",
            121,
            "P",
            ["the-thao", "hai"],
            "Xã nhỏ không đủ người, đội bóng làng mười năm chưa thắng trận nào nhưng vẫn nhất quyết góp mặt ở cúp huyện. Huấn luyện viên trẻ tình nguyện phải ghép ông giáo thể dục, anh hàng xáo và thằng nhóc mười lăm tuổi thành một đội hình. Phim thể thao hài hước về những kẻ toàn thua nhưng chưa từng bỏ cuộc.")
    ];

    public static readonly DiscoveryEvalCinema[] Cinemas =
    [
        new("Beta Mỹ Đình", "Hà Nội"),
        new("Lotte Hà Đông", "Hà Nội"),
        new("Galaxy Nguyễn Du", "TP. Hồ Chí Minh"),
        new("CGV Vincom Thủ Đức", "TP. Hồ Chí Minh")
    ];
}

/// <summary>
/// Graded result of one eval turn. A field left null/empty means "not asserted" (inheritance and
/// carry-over stay outside the assertion). <see cref="ClearedFields"/> reuses
/// <see cref="DiscoveryFilterFields"/> names. An expectation with every field unset on a
/// <c>filter</c> scenario asserts a full reset: the resulting state must be completely empty.
/// </summary>
internal sealed record DiscoveryEvalExpectation(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? City,
    string? CinemaName,
    decimal? MaxPrice,
    int? MinSeats,
    int? MaxDurationMinutes,
    string[] GenreSlugs,
    string[] ClearedFields,
    string[] MovieTitleKeywords);

/// <summary>One graded discovery prompt. <see cref="Kind"/> is "filter" or "semantic".</summary>
internal sealed record DiscoveryEvalScenario(
    int Id,
    string Kind,
    string Prompt,
    DiscoveryEvalExpectation Expect,
    string Note);

/// <summary>
/// The 40-scenario eval set. All relative dates are pinned to
/// <see cref="FixedNowUtcIso"/> (Saturday 2026-11-14, 10:00 UTC+7) and written as concrete
/// DateOnly values with short comments.
/// </summary>
internal static class DiscoveryEvalSet
{
    public const string FixedNowUtcIso = "2026-11-14T03:00:00Z";

    public static readonly DiscoveryEvalScenario[] Scenarios =
    [
        // --- Lọc điều kiện (1..20) ---
        Filter(1, "Cho mình xem các suất chiếu đang có đi.", Expect(),
            "Tìm kiếm mới không điều kiện: kỳ vọng rỗng hoàn toàn là chủ đích; cửa sổ 7 ngày mặc định áp dụng lúc truy vấn, không nằm trong bộ lọc."),
        Filter(2, "Tối thứ Bảy này có phim gì hay không?",
            Expect(dateFrom: new DateOnly(2026, 11, 14), dateTo: new DateOnly(2026, 11, 14)), // thứ Bảy của mốc chốt
            "Khung giờ tối không assert vì kỳ vọng không có trường cửa sổ giờ."),
        Filter(3, "Chủ nhật tới có lịch chiếu gì vậy?",
            Expect(dateFrom: new DateOnly(2026, 11, 15), dateTo: new DateOnly(2026, 11, 15)), // chủ nhật tới
            "Chủ nhật tới = 15/11/2026."),
        Filter(4, "Cuối tuần này có gì đáng xem không?",
            Expect(dateFrom: new DateOnly(2026, 11, 14), dateTo: new DateOnly(2026, 11, 15)), // thứ Bảy -> chủ nhật
            "Cuối tuần này: thứ Bảy 14/11 đến chủ nhật 15/11/2026."),
        Filter(5, "Ngày mai mình rảnh, cho mình xin suất nào đi.",
            Expect(dateFrom: new DateOnly(2026, 11, 15), dateTo: new DateOnly(2026, 11, 15)), // ngày mai
            "Ngày mai = 15/11/2026."),
        Filter(6, "Từ 20/11 đến 25/11 có suất nào không?",
            Expect(dateFrom: new DateOnly(2026, 11, 20), dateTo: new DateOnly(2026, 11, 25)),
            "Khoảng ngày tường minh, không phải ngày tương đối."),
        Filter(7, "Ở TP. Hồ Chí Minh đang có phim gì vậy?",
            Expect(city: "TP. Hồ Chí Minh"),
            "Chỉ lọc thành phố."),
        Filter(8, "Galaxy Nguyễn Du có phim gì đang chiếu?",
            Expect(cinemaName: "Galaxy Nguyễn Du"),
            "Chỉ chọn rạp, không đụng tới thành phố."),
        Filter(9, "Chuyển sang Lotte Hà Đông đi.",
            Expect(cinemaName: "Lotte Hà Đông"),
            "Đổi rạp giữa hội thoại; các điều kiện khác kế thừa nên chỉ assert CinemaName."),
        Filter(10, "Có suất nào dưới 120 nghìn không?",
            Expect(maxPrice: 120_000m),
            "120 nghìn = 120000 đ."),
        Filter(11, "Chỉ xem suất dưới 90 nghìn thôi nhé.",
            Expect(maxPrice: 90_000m),
            "90 nghìn = 90000 đ."),
        Filter(12, "Bỏ giới hạn giá đi.",
            Expect(clearedFields: ["maxPrice"]),
            "Xoá trường maxPrice; các điều kiện còn lại giữ nguyên."),
        Filter(13, "Cần đủ 3 ghế ngồi cạnh nhau.",
            Expect(minSeats: 3),
            "Số ghế tối thiểu 3."),
        Filter(14, "Nhóm mình cần đủ 8 người, còn ghế không?",
            Expect(minSeats: 8),
            "8 là trần trên của MinSeats (1..8)."),
        Filter(15, "Cho mình phim dưới 2 tiếng thôi.",
            Expect(maxDurationMinutes: 120),
            "2 tiếng = 120 phút."),
        Filter(16, "Phim ngắn dưới 90 phút có không?",
            Expect(maxDurationMinutes: 90),
            "Phim ngắn dưới 90 phút."),
        Filter(17, "Có phim kinh dị nào không?",
            Expect(genreSlugs: ["kinh-di"]),
            "Chỉ lọc thể loại kinh-di."),
        Filter(18, "Phim kinh dị ở Hà Nội tối thứ Bảy đủ 2 ghế dưới 100 nghìn nhé.",
            Expect(
                dateFrom: new DateOnly(2026, 11, 14), dateTo: new DateOnly(2026, 11, 14), // tối thứ Bảy
                city: "Hà Nội",
                maxPrice: 100_000m,
                minSeats: 2,
                genreSlugs: ["kinh-di"]),
            "Một lượt kết hợp nhiều trường: thể loại, thành phố, ngày, ghế, giá."),
        Filter(19, "Tìm phim khác từ đầu đi.", Expect(),
            "Reset toàn bộ (intent new_search): kỳ vọng rỗng hoàn toàn là chủ đích, không dùng ClearedFields vì mọi trường đều bị xoá."),
        Filter(20, "Chỉ lấy suất ở TP. Hồ Chí Minh nhé.",
            Expect(city: "TP. Hồ Chí Minh"),
            "Lượt kế thừa: chỉ assert City; các điều kiện khác kế thừa từ trạng thái trước."),

        // --- Chủ đề phim (21..40) ---
        Semantic(21, "Có phim nào về du hành thời gian không?",
            Expect(movieTitleKeywords: ["Vòng Xoáy Thời Gian"]),
            "Chủ đề du hành thời gian khớp 'Vòng Xoáy Thời Gian'."),
        Semantic(22, "Mình muốn xem phim gia đình ấm áp một chút.",
            Expect(movieTitleKeywords: ["Mùa Hè Của Bé Thư"]),
            "Chủ đề gia đình ấm áp khớp 'Mùa Hè Của Bé Thư'."),
        Semantic(23, "Gợi ý phim hoạt hình cho con nhỏ đi.",
            Expect(movieTitleKeywords: ["Gấu Bông Phiêu Lưu Ký"]),
            "Hoạt hình cho con nhỏ khớp 'Gấu Bông Phiêu Lưu Ký'."),
        Semantic(24, "Có phim hành động giật gân nào mới ra không?",
            Expect(movieTitleKeywords: ["Truy Tìm Bóng Đêm"]),
            "Hành động giật gân khớp 'Truy Tìm Bóng Đêm'."),
        Semantic(25, "Muốn xem phim ma quái kinh dị, có gợi ý không?",
            Expect(movieTitleKeywords: ["Lời Thề Quỷ Án"]),
            "Ma quái, kinh dị khớp 'Lời Thề Quỷ Án'."),
        Semantic(26, "Cho mình một bộ phim hài hước nhẹ nhàng để thư giãn.",
            Expect(movieTitleKeywords: ["Cưới Kịp Chạy"]),
            "Hài hước nhẹ nhàng khớp 'Cưới Kịp Chạy'."),
        Semantic(27, "Có phim tình cảm mùa đông nào không?",
            Expect(movieTitleKeywords: ["Hà Nội Mùa Đông Đó"]),
            "Tình cảm mùa đông khớp 'Hà Nội Mùa Đông Đó'."),
        Semantic(28, "Mình muốn xem phim về thiên nhiên, rừng núi hoang sơ.",
            Expect(movieTitleKeywords: ["Rừng Trường Sơn"]),
            "Thiên nhiên, rừng núi khớp 'Rừng Trường Sơn'."),
        Semantic(29, "Gợi ý phim chiến tranh lịch sử đi.",
            Expect(movieTitleKeywords: ["Đường Lên Định Quan 1968"]),
            "Chiến tranh lịch sử khớp 'Đường Lên Định Quan 1968'."),
        Semantic(30, "Có phim nào về âm nhạc không?",
            Expect(movieTitleKeywords: ["Nhịp Trống Sài Gòn"]),
            "Âm nhạc khớp 'Nhịp Trống Sài Gòn'."),
        Semantic(31, "Gợi ý mình phim điều tra án mạng đi.",
            Expect(movieTitleKeywords: ["Hồ Sơ Vụ Án Số 7"]),
            "Điều tra án mạng khớp 'Hồ Sơ Vụ Án Số 7'."),
        Semantic(32, "Muốn xem phim về bóng đá, thể thao ấy.",
            Expect(movieTitleKeywords: ["Sân Cỏ Không Cầu Thủ"]),
            "Bóng đá, thể thao khớp 'Sân Cỏ Không Cầu Thủ'."),
        Semantic(33, "Phim nào kể về người quay ngược thời gian về quá khứ vậy?",
            Expect(movieTitleKeywords: ["Vòng Xoáy Thời Gian"]),
            "Cách nói khác của du hành thời gian: quay ngược thời gian về quá khứ."),
        Semantic(34, "Nhà mình muốn đi xem một bộ phim ấm áp, cả nhà cùng con nhỏ thì nên chọn phim gì đây?",
            Expect(movieTitleKeywords: ["Mùa Hè Của Bé Thư"]),
            "Câu dài đời thường cho chủ đề gia đình ấm áp, xem cùng con nhỏ."),
        Semantic(35, "Có phim hoạt hình nào cho bé mẫu giáo xem cùng bố mẹ không?",
            Expect(movieTitleKeywords: ["Gấu Bông Phiêu Lưu Ký"]),
            "Cách nói khác của hoạt hình cho con nhỏ: bé mẫu giáo xem cùng bố mẹ."),
        Semantic(36, "Mình muốn xem phim ma quỷ, xem xong nổi da gà luôn ấy.",
            Expect(movieTitleKeywords: ["Lời Thề Quỷ Án"]),
            "Từ đồng nghĩa của ma quái kinh dị: nổi da gà."),
        Semantic(37, "Cho mình một bộ phim nói về chiến tranh, về các chiến sĩ năm xưa với lòng yêu nước.",
            Expect(movieTitleKeywords: ["Đường Lên Định Quan 1968"]),
            "Câu dài đời thường cho chủ đề chiến tranh lịch sử."),
        Semantic(38, "Có phim hài nào dưới 2 tiếng không?",
            Expect(maxDurationMinutes: 120, movieTitleKeywords: ["Cưới Kịp Chạy"]),
            "Chủ đề hài + ràng buộc thời lượng 120 phút; 'Cưới Kịp Chạy' dài 95 phút."),
        Semantic(39, "Phim hoạt hình cho bé, dưới 100 phút thôi nha.",
            Expect(maxDurationMinutes: 100, movieTitleKeywords: ["Gấu Bông Phiêu Lưu Ký"]),
            "Chủ đề hoạt hình cho bé + ràng buộc 100 phút; 'Gấu Bông Phiêu Lưu Ký' dài 88 phút."),
        Semantic(40, "Phim về chiến tranh đang chiếu ở Hà Nội có không?",
            Expect(city: "Hà Nội", movieTitleKeywords: ["Đường Lên Định Quan 1968"]),
            "Chủ đề chiến tranh + ràng buộc thành phố Hà Nội.")
    ];

    private static DiscoveryEvalScenario Filter(int id, string prompt, DiscoveryEvalExpectation expect, string note) =>
        new(id, "filter", prompt, expect, note);

    private static DiscoveryEvalScenario Semantic(int id, string prompt, DiscoveryEvalExpectation expect, string note) =>
        new(id, "semantic", prompt, expect, note);

    private static DiscoveryEvalExpectation Expect(
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? city = null,
        string? cinemaName = null,
        decimal? maxPrice = null,
        int? minSeats = null,
        int? maxDurationMinutes = null,
        string[]? genreSlugs = null,
        string[]? clearedFields = null,
        string[]? movieTitleKeywords = null) =>
        new(dateFrom, dateTo, city, cinemaName, maxPrice, minSeats, maxDurationMinutes,
            genreSlugs ?? [], clearedFields ?? [], movieTitleKeywords ?? []);
}

/// <summary>Data-integrity tests over the static eval set; no database and no collection involved.</summary>
public sealed class DiscoveryEvalScenarioTests
{
    [Fact]
    public void Eval_set_has_forty_scenarios_with_twenty_per_kind()
    {
        Assert.Equal(40, DiscoveryEvalSet.Scenarios.Length);
        Assert.Equal(20, DiscoveryEvalSet.Scenarios.Count(s => s.Kind == "filter"));
        Assert.Equal(20, DiscoveryEvalSet.Scenarios.Count(s => s.Kind == "semantic"));
        Assert.All(DiscoveryEvalSet.Scenarios, s => Assert.True(s.Kind is "filter" or "semantic", $"Scenario {s.Id}: unknown kind \"{s.Kind}\"."));
    }

    [Fact]
    public void Scenario_ids_are_unique_and_sequential_from_one()
    {
        Assert.Equal(Enumerable.Range(1, 40), DiscoveryEvalSet.Scenarios.Select(s => s.Id));
    }

    [Fact]
    public void Prompts_are_trimmed_short_and_unique()
    {
        var prompts = DiscoveryEvalSet.Scenarios.Select(s => s.Prompt).ToArray();
        Assert.All(prompts, p => Assert.True(p.Length > 0, "Prompt must not be empty."));
        Assert.All(prompts, p => Assert.Equal(p, p.Trim(), StringComparer.Ordinal));
        Assert.All(prompts, p => Assert.True(p.Length <= 1000, "Prompt must stay under 1000 characters."));
        Assert.Equal(prompts.Length, prompts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Expectations_stay_inside_discovery_ranges()
    {
        foreach (var scenario in DiscoveryEvalSet.Scenarios)
        {
            var e = scenario.Expect;
            if (e.MinSeats is { } seats)
            {
                Assert.InRange(seats, DiscoveryFilters.MinSeatsFloor, DiscoveryFilters.MinSeatsCeiling);
            }

            if (e.MaxPrice is { } price)
            {
                Assert.True(price > 0, $"Scenario {scenario.Id}: MaxPrice must be positive.");
                Assert.True(price <= 1_000_000m, $"Scenario {scenario.Id}: MaxPrice must stay under 1,000,000.");
            }

            if (e.DateFrom is { } from && e.DateTo is { } to)
            {
                Assert.InRange(to.DayNumber - from.DayNumber, 0, DiscoveryTime.MaxDateRangeDays - 1);
            }
        }
    }

    [Fact]
    public void Cleared_fields_and_genre_slugs_use_known_vocabulary()
    {
        var catalogGenres = DiscoveryEvalCatalog.Movies.SelectMany(m => m.GenreSlugs).ToHashSet(StringComparer.Ordinal);
        foreach (var scenario in DiscoveryEvalSet.Scenarios)
        {
            Assert.All(scenario.Expect.ClearedFields, f =>
                Assert.True(DiscoveryFilterFields.IsDefined(f), $"Scenario {scenario.Id}: unknown cleared field \"{f}\"."));
            Assert.All(scenario.Expect.GenreSlugs, g =>
                Assert.True(catalogGenres.Contains(g), $"Scenario {scenario.Id}: genre \"{g}\" is not in the eval catalog."));
        }
    }

    [Fact]
    public void Filter_scenarios_assert_at_least_one_expectation_field()
    {
        var fullResetIds = new List<int>();
        foreach (var scenario in DiscoveryEvalSet.Scenarios.Where(s => s.Kind == "filter"))
        {
            var e = scenario.Expect;
            var assertsSomething =
                e.DateFrom is not null || e.DateTo is not null || e.City is not null ||
                e.CinemaName is not null || e.MaxPrice is not null || e.MinSeats is not null ||
                e.MaxDurationMinutes is not null || e.GenreSlugs.Length > 0 ||
                e.ClearedFields.Length > 0 || e.MovieTitleKeywords.Length > 0;
            if (!assertsSomething)
            {
                fullResetIds.Add(scenario.Id);
                Assert.True(scenario.Note.Length > 0, $"Scenario {scenario.Id}: an all-empty expectation must be a documented full reset.");
            }
        }

        // Riêng lượt tìm kiếm mới (1) và lượt tìm lại từ đầu (19) kỳ vọng rỗng hoàn toàn.
        Assert.Equal(2, fullResetIds.Count);
        Assert.Contains(1, fullResetIds);
        Assert.Contains(19, fullResetIds);
    }

    [Fact]
    public void Semantic_scenarios_name_at_least_one_movie()
    {
        foreach (var scenario in DiscoveryEvalSet.Scenarios.Where(s => s.Kind == "semantic"))
        {
            Assert.True(scenario.Expect.MovieTitleKeywords.Length > 0, $"Scenario {scenario.Id} (semantic) needs at least one movie keyword.");
        }
    }

    [Fact]
    public void Catalog_has_twelve_distinct_valid_movies()
    {
        var movies = DiscoveryEvalCatalog.Movies;
        Assert.Equal(12, movies.Length);
        Assert.Equal(movies.Length, movies.Select(m => m.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(movies.Length, movies.Select(m => m.Title).Distinct(StringComparer.Ordinal).Count());
        Assert.All(movies, m => Assert.InRange(m.DurationMinutes, 60, 200));
        Assert.All(movies, m => Assert.True(m.GenreSlugs.Length > 0, $"Movie {m.Key} needs at least one genre."));
        Assert.All(movies, m => Assert.All(m.GenreSlugs, g => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", g)));
        Assert.All(movies, m => Assert.True(m.Synopsis.Length >= 40, $"Movie {m.Key} synopsis is too short."));
    }

    [Fact]
    public void Catalog_has_four_cinemas_across_two_cities()
    {
        var cinemas = DiscoveryEvalCatalog.Cinemas;
        Assert.Equal(4, cinemas.Length);
        Assert.Equal(cinemas.Length, cinemas.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, cinemas.Select(c => c.City).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Movie_title_keywords_resolve_into_catalog_titles()
    {
        var titles = DiscoveryEvalCatalog.Movies.Select(m => m.Title).ToArray();
        foreach (var scenario in DiscoveryEvalSet.Scenarios)
        {
            foreach (var keyword in scenario.Expect.MovieTitleKeywords)
            {
                Assert.True(titles.Any(t => t.Contains(keyword, StringComparison.Ordinal)),
                    $"Scenario {scenario.Id}: keyword \"{keyword}\" matches no catalog title.");
            }
        }
    }

    [Fact]
    public void Fixed_now_is_saturday_november_14_2026_in_utc7()
    {
        var now = DateTime.Parse(DiscoveryEvalSet.FixedNowUtcIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.Equal(new DateTime(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc), now);
        var today = DiscoveryTime.TodayInZone(now);
        Assert.Equal(new DateOnly(2026, 11, 14), today);
        Assert.Equal(DayOfWeek.Saturday, today.DayOfWeek);
    }
}
