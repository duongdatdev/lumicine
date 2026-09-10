using LumiCine.Domain.Bookings;
using LumiCine.Domain.Catalog;
using LumiCine.Domain.Cinemas;
using LumiCine.Domain.Scheduling;
using LumiCine.Application.Accounts;
using LumiCine.Infrastructure.Identity;
using LumiCine.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Persistence;

public sealed class LumiCineDbContext(DbContextOptions<LumiCineDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<MovieGenre> MovieGenres => Set<MovieGenre>();
    public DbSet<Cinema> Cinemas => Set<Cinema>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Showtime> Showtimes => Set<Showtime>();
    public DbSet<ShowtimeSeat> ShowtimeSeats => Set<ShowtimeSeat>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingItem> BookingItems => Set<BookingItem>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(user => user.Email).IsRequired().HasMaxLength(254);
            entity.Property(user => user.NormalizedEmail).HasMaxLength(254);
            entity.Property(user => user.DisplayName).IsRequired().HasMaxLength(100);
            entity.Property(user => user.IsActive).IsRequired();
            entity.Property(user => user.CreatedAtUtc).IsRequired();
            entity.HasIndex(user => user.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        });
        modelBuilder.Entity<IdentityRole<Guid>>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasData(
                new IdentityRole<Guid>
                {
                    Id = Guid.Parse("71000000-0000-0000-0000-000000000001"),
                    Name = RoleNames.Customer,
                    NormalizedName = "CUSTOMER",
                    ConcurrencyStamp = "lumicine-role-customer-v1"
                },
                new IdentityRole<Guid>
                {
                    Id = Guid.Parse("71000000-0000-0000-0000-000000000002"),
                    Name = RoleNames.Staff,
                    NormalizedName = "STAFF",
                    ConcurrencyStamp = "lumicine-role-staff-v1"
                },
                new IdentityRole<Guid>
                {
                    Id = Guid.Parse("71000000-0000-0000-0000-000000000003"),
                    Name = RoleNames.Admin,
                    NormalizedName = "ADMIN",
                    ConcurrencyStamp = "lumicine-role-admin-v1"
                });
        });
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LumiCineDbContext).Assembly);
    }
}
