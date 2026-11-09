using System.Net;
using System.Text;
using System.Text.Json;
using LumiCine.Application.Accounts;
using LumiCine.Application.Reporting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LumiCine.IntegrationTests.Reporting;

public sealed class AdminReportingEndpointTests
{
    private static readonly string[] Routes =
    [
        "/api/admin/dashboard/summary", "/api/admin/dashboard/operations",
        "/api/admin/reports/revenue", "/api/admin/reports/showtimes",
        "/api/admin/reports/revenue/export", "/api/admin/reports/showtimes/export"
    ];

    [Fact]
    public async Task All_routes_require_admin_and_never_cache_auth_responses()
    {
        using var factory = CreateFactory(out _);
        using var client = CreateHttpsClient(factory);
        foreach (var route in Routes)
        {
            using var guest = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, guest.StatusCode);
            Assert.Contains("no-store", guest.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        foreach (var role in new[] { RoleNames.Customer, RoleNames.Staff })
        {
            SetRole(client, role, role);
            foreach (var route in Routes)
            {
                using var response = await client.GetAsync(route);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Date_filters_require_iso_shape_even_when_complex_binding_uses_prefix()
    {
        using var factory = CreateFactory(out _);
        using var client = CreateHttpsClient(factory);
        SetRole(client, RoleNames.Admin, "date-admin");

        using var invalid = await client.GetAsync("/api/admin/reports/revenue?filter.fromDate=2026-1-1&filter.toDate=2026-01-02");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("no-store", invalid.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);

        using var valid = await client.GetAsync("/api/admin/reports/revenue?filter.fromDate=2026-01-01&filter.toDate=2026-01-02");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var body = await JsonDocument.ParseAsync(await valid.Content.ReadAsStreamAsync());
        Assert.Equal(1m, body.RootElement.GetProperty("totals").GetProperty("ticketSalesAmount").GetDecimal());
    }

    [Fact]
    public async Task Revenue_export_is_utf8_bom_crlf_and_neutralizes_text_without_changing_negative_money()
    {
        using var factory = CreateFactory(out _);
        using var client = CreateHttpsClient(factory);
        SetRole(client, RoleNames.Admin, "csv-admin");

        using var response = await client.GetAsync("/api/admin/reports/revenue/export?fromDate=2026-01-01&toDate=2026-01-01");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("X-Report-Row-Count").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("\r\n", csv);
        Assert.DoesNotContain("\n", csv.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.Contains("\"'=HYPERLINK(1)\"", csv);
        Assert.Contains("\"-5\"", csv);
    }

    [Fact]
    public async Task Export_row_overflow_returns_422_before_file_headers_or_body()
    {
        using var factory = CreateFactory(out _, overflowRevenueRows: true);
        using var client = CreateHttpsClient(factory);
        SetRole(client, RoleNames.Admin, "overflow-admin");

        using var response = await client.GetAsync("/api/admin/reports/revenue/export");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Report-Generated-At"));
        Assert.False(response.Headers.Contains("X-Report-Row-Count"));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Export_passes_request_aborted_token_to_reporting_service()
    {
        using var factory = CreateFactory(out var stub, waitForExportCancellation: true);
        using var client = CreateHttpsClient(factory);
        SetRole(client, RoleNames.Admin, "cancel-admin");
        using var requestCancellation = new CancellationTokenSource();

        var request = client.GetAsync("/api/admin/reports/revenue/export", HttpCompletionOption.ResponseHeadersRead,
            requestCancellation.Token);
        await stub.ExportStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        requestCancellation.Cancel();
        try { using var response = await request; }
        catch (OperationCanceledException) { }
        Assert.True(stub.LastExportCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Export_limit_is_shared_by_both_routes_and_partitioned_per_admin()
    {
        using var factory = CreateFactory(out _);
        using var client = CreateHttpsClient(factory);
        SetRole(client, RoleNames.Admin, "rate-admin-1");

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync(i % 2 == 0
                ? "/api/admin/reports/revenue/export"
                : "/api/admin/reports/showtimes/export");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var limited = await client.GetAsync("/api/admin/reports/revenue/export"))
        {
            Assert.Equal((HttpStatusCode)429, limited.StatusCode);
            Assert.Contains("no-store", limited.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        SetRole(client, RoleNames.Admin, "rate-admin-2");
        using var otherAdmin = await client.GetAsync("/api/admin/reports/showtimes/export");
        Assert.Equal(HttpStatusCode.OK, otherAdmin.StatusCode);
    }

    private static ReportingApiFactory CreateFactory(out StubAdminReportingService stub,
        bool overflowRevenueRows = false, bool waitForExportCancellation = false)
    {
        stub = new StubAdminReportingService(overflowRevenueRows, waitForExportCancellation);
        return new ReportingApiFactory(stub);
    }

    private static HttpClient CreateHttpsClient(ReportingApiFactory factory) =>
        factory.App.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static void SetRole(HttpClient client, string role, string subject)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Remove("X-Test-Sub");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Sub", subject);
    }

    private sealed class ReportingApiFactory : IDisposable
    {
        private readonly ApiWebApplicationFactory _baseFactory = new();
        public WebApplicationFactory<Program> App { get; }

        public ReportingApiFactory(StubAdminReportingService stub)
        {
            App = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IAdminReportingService>(stub));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = ReportingTestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = ReportingTestAuthHandler.SchemeName;
                    options.DefaultForbidScheme = ReportingTestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, ReportingTestAuthHandler>(ReportingTestAuthHandler.SchemeName, _ => { });
            }));
        }

        public void Dispose()
        {
            App.Dispose();
            _baseFactory.Dispose();
        }
    }

    private sealed class StubAdminReportingService(bool overflowRevenueRows = false, bool waitForExportCancellation = false) : IAdminReportingService
    {
        private static readonly ReportMetadata Metadata = new(DateTime.SpecifyKind(new DateTime(2026, 1, 2), DateTimeKind.Utc),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
        private static readonly RevenueReportRow[] RevenueRows = [new("=HYPERLINK(1)", "=HYPERLINK(1)", 1m, 2m, 7m, -5m, 1, 0)];
        private readonly IReadOnlyList<RevenueReportRow> _exportRows = overflowRevenueRows
            ? Enumerable.Repeat(RevenueRows[0], ReportExportLimits.MaxRows + 1).ToArray()
            : RevenueRows;
        public TaskCompletionSource ExportStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken LastExportCancellationToken { get; private set; }

        public Task<AdminDashboardSummary> GetSummaryAsync(ReportFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new AdminDashboardSummary(Metadata, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>(), 0, 0, 0, new Dictionary<string, int>()));
        public Task<AdminDashboardOperations> GetOperationsAsync(ReportFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new AdminDashboardOperations(DateTime.UtcNow, filter.MovieId, filter.CinemaId, 0, 0, [], 0, [], 0, [], 0, [], 0, [], 0, []));
        public Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> GetRevenueAsync(RevenueReportFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new AdminReportResult<RevenueReportRow, RevenueReportTotals>(Metadata, RevenueRows, RevenueRows.Length, filter.Page, filter.PageSize,
                new RevenueReportTotals(1, 2, 7, -5, 1, 0)));
        public Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> GetShowtimesAsync(ReportFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>(Metadata, [], 0, filter.Page, filter.PageSize,
                new ShowtimeReportTotals(0, 0, 0, 0, 0, 0, 0, 0, 0)));
        public Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> GetRevenueExportAsync(RevenueReportFilter filter, CancellationToken cancellationToken) =>
            WaitForCancellationOrReturnAsync(cancellationToken);
        private async Task<AdminReportResult<RevenueReportRow, RevenueReportTotals>> WaitForCancellationOrReturnAsync(CancellationToken cancellationToken)
        {
            if (waitForExportCancellation)
            {
                LastExportCancellationToken = cancellationToken;
                ExportStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new AdminReportResult<RevenueReportRow, RevenueReportTotals>(Metadata, _exportRows, _exportRows.Count, 1, _exportRows.Count,
                new RevenueReportTotals(1, 2, 7, -5, 1, 0));
        }
        public Task<AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>> GetShowtimesExportAsync(ReportFilter filter, CancellationToken cancellationToken) =>
            Task.FromResult(new AdminReportResult<ShowtimeReportRow, ShowtimeReportTotals>(Metadata, [], 0, 1, 0,
                new ShowtimeReportTotals(0, 0, 0, 0, 0, 0, 0, 0, 0)));
    }
}
