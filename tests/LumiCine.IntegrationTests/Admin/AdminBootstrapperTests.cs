using System.Data.Common;
using LumiCine.API;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Persistence;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Admin;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class AdminBootstrapperTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Seed_creates_an_active_confirmed_admin_and_is_idempotent()
    {
        using var factory = CreateFactory();
        var configuration = CreateConfiguration("bootstrap@example.test", "InitPass!234");
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            Assert.True(await bootstrapper.SeedAsync(configuration));
            Assert.False(await bootstrapper.SeedAsync(CreateConfiguration("bootstrap@example.test", "OtherPass!234")));
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync("bootstrap@example.test");
            Assert.NotNull(user);
            userId = user.Id;
            Assert.True(user.IsActive);
            Assert.True(user.EmailConfirmed);
            Assert.True(await userManager.CheckPasswordAsync(user, "InitPass!234"));
            Assert.False(await userManager.CheckPasswordAsync(user, "OtherPass!234"));
            Assert.True(await userManager.IsInRoleAsync(user, RoleNames.Admin));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString("D"));
            Assert.NotNull(user);
            user.IsActive = false;
            user.EmailConfirmed = false;
            Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            Assert.False(await bootstrapper.SeedAsync(configuration));
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(userId.ToString("D"));
            Assert.NotNull(user);
            Assert.False(user.IsActive);
            Assert.False(user.EmailConfirmed);
            Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .CheckPasswordAsync(user, "InitPass!234"));
        }
    }

    [Fact]
    public async Task Existing_non_admin_email_is_not_promoted()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = new ApplicationUser
        {
            UserName = "collision@example.test",
            Email = "collision@example.test",
            EmailConfirmed = true,
            DisplayName = "Existing customer"
        };
        Assert.True((await userManager.CreateAsync(existing, "CustomerPass!234")).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(existing, RoleNames.Customer)).Succeeded);

        var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bootstrapper.SeedAsync(CreateConfiguration("collision@example.test", "AdminPass!234")));

        Assert.Contains("non-admin", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await userManager.IsInRoleAsync(existing, RoleNames.Customer));
        Assert.False(await userManager.IsInRoleAsync(existing, RoleNames.Admin));
        Assert.True(await userManager.CheckPasswordAsync(existing, "CustomerPass!234"));
    }

    [Fact]
    public async Task Missing_bootstrap_settings_fail_before_database_access()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bootstrapper.SeedAsync(CreateConfiguration("admin@example.test", null)));

        Assert.Equal("BootstrapAdmin:Password must be configured and at most 128 characters.", error.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidBootstrapSettings))]
    public async Task Invalid_bootstrap_email_or_password_fails_before_database_access(string email, string password)
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bootstrapper.SeedAsync(CreateConfiguration(email, password)));

        Assert.DoesNotContain(email, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(password, error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> InvalidBootstrapSettings => new()
    {
        { "not-an-email", "AdminPass!234" },
        { $"{new string('a', 64)}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', 63)}", "AdminPass!234" },
        { "admin@example.test", new string('A', 126) + "a1!" }
    };

    [Fact]
    public async Task Role_assignment_failure_rolls_back_new_admin_creation()
    {
        using var factory = CreateFactory();
        var interceptor = new FailOnRoleInsertInterceptor();
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<LumiCineDbContext>((_, options) => options.AddInterceptors(interceptor))));
        interceptor.Enabled = true;
        using (var scope = failingFactory.Services.CreateScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                bootstrapper.SeedAsync(CreateConfiguration("rollback@example.test", "AdminPass!234")));
        }

        interceptor.Enabled = false;
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Users.AnyAsync(user => user.Email == "rollback@example.test"));
    }

    private ApiWebApplicationFactory CreateFactory() => new(connectionString: fixture.ConnectionString);

    private static IConfiguration CreateConfiguration(string? email, string? password) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BootstrapAdmin:Email"] = email,
            ["BootstrapAdmin:Password"] = password
        }).Build();

    private sealed class FailOnRoleInsertInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ThrowIfRoleInsert(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfRoleInsert(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfRoleInsert(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfRoleInsert(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfRoleInsert(DbCommand command)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO \"UserRoles\"", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Injected role insert failure.");
            }
        }
    }
}

public sealed class StartupCommandLineTests
{
    [Fact]
    public void Seed_admin_flag_is_consumed_before_builder_receives_arguments()
    {
        var enabled = StartupCommandLine.ConsumeSeedAdminFlag(
            ["--urls", "http://localhost:5000", "--seed-admin", "--environment", "Development"],
            out var builderArgs);

        Assert.True(enabled);
        Assert.Equal(["--urls", "http://localhost:5000", "--environment", "Development"], builderArgs);
    }

    [Fact]
    public void Normal_startup_arguments_are_preserved_without_seed_flag()
    {
        var enabled = StartupCommandLine.ConsumeSeedAdminFlag(["--urls", "http://localhost:5000"], out var builderArgs);

        Assert.False(enabled);
        Assert.Equal(["--urls", "http://localhost:5000"], builderArgs);
    }
}
