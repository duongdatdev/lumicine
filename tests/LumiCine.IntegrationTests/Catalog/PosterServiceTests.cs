using LumiCine.Application.Common;
using LumiCine.Application.Media;
using LumiCine.Domain.Catalog;
using LumiCine.Infrastructure.Media;
using LumiCine.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace LumiCine.IntegrationTests.Catalog;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class PosterServiceTests(PostgresDatabaseFixture database)
{
    [Fact]
    public async Task Upload_reencodes_and_replace_delete_manage_only_uuid_files()
    {
        var directory = NewDirectory();
        try
        {
            await using var db = database.CreateDbContext();
            var movie = await CreateMovie(db);
            var service = CreateService(db, directory);
            var firstUrl = await Upload(service, movie.Id, Encode(SKEncodedImageFormat.Png, 3200, 1800), "image/png");
            var firstId = Guid.Parse(firstUrl.Split('/').Last());
            Assert.True(File.Exists(Path.Combine(directory, $"{firstId:N}.jpg")));

            var secondUrl = await Upload(service, movie.Id, Encode(SKEncodedImageFormat.Jpeg, 400, 300), "image/jpeg");
            var secondId = Guid.Parse(secondUrl.Split('/').Last());
            Assert.False(File.Exists(Path.Combine(directory, $"{firstId:N}.jpg")));
            Assert.True(File.Exists(Path.Combine(directory, $"{secondId:N}.jpg")));
            var poster = await service.OpenAsync(secondId, default);
            Assert.Equal("image/jpeg", poster!.ContentType);
            using (poster.Content)
            using (var codec = SKCodec.Create(poster.Content))
            {
                Assert.Equal(SKEncodedImageFormat.Jpeg, codec!.EncodedFormat);
                Assert.True(codec.Info.Width <= 1600);
                Assert.True(codec.Info.Height <= 1600);
            }

            Assert.True(await service.DeleteAsync(movie.Id, default));
            Assert.Null((await db.Movies.SingleAsync(m => m.Id == movie.Id)).PosterUrl);
            Assert.False(File.Exists(Path.Combine(directory, $"{secondId:N}.jpg")));
            Assert.Null(await service.OpenAsync(Guid.NewGuid(), default));
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task Upload_rejects_a_file_over_five_mib()
    {
        var directory = NewDirectory();
        try
        {
            await using var db = database.CreateDbContext();
            var movie = await CreateMovie(db);
            var service = CreateService(db, directory);

            var exception = await Assert.ThrowsAsync<ApiException>(() =>
                Upload(service, movie.Id, new byte[5 * 1024 * 1024 + 1], "image/jpeg"));

            Assert.Equal(413, exception.StatusCode);
            Assert.Null((await db.Movies.SingleAsync(m => m.Id == movie.Id)).PosterUrl);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task Database_failure_rolls_back_movie_url_and_removes_the_new_file()
    {
        var directory = NewDirectory();
        try
        {
            Guid movieId;
            await using (var setup = database.CreateDbContext())
            {
                var movie = await CreateMovie(setup);
                var regularService = CreateService(setup, directory);
                var oldUrl = await Upload(regularService, movie.Id, Encode(SKEncodedImageFormat.Jpeg, 32, 24), "image/jpeg");
                movieId = movie.Id;
                Assert.NotNull(oldUrl);
            }
            await using var check = database.CreateDbContext();
            var oldPosterUrl = (await check.Movies.SingleAsync(m => m.Id == movieId)).PosterUrl;
            var options = new DbContextOptionsBuilder<LumiCine.Infrastructure.Persistence.LumiCineDbContext>()
                .UseNpgsql(database.ConnectionString)
                .AddInterceptors(new FailingSaveInterceptor())
                .Options;
            await using (var db = new LumiCine.Infrastructure.Persistence.LumiCineDbContext(options))
            {
                var service = CreateService(db, directory);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Upload(service, movieId, Encode(SKEncodedImageFormat.Png, 32, 24), "image/png"));
            }

            await using var verify = database.CreateDbContext();
            Assert.Equal(oldPosterUrl, (await verify.Movies.SingleAsync(m => m.Id == movieId)).PosterUrl);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { DeleteDirectory(directory); }
    }

    [Fact]
    public async Task Disk_failure_leaves_the_existing_poster_and_database_url_unchanged()
    {
        var parent = NewDirectory();
        var filePath = Path.Combine(parent, "not-a-directory");
        await File.WriteAllTextAsync(filePath, "block directory creation");
        try
        {
            await using var db = database.CreateDbContext();
            var movie = await CreateMovie(db);
            var service = CreateService(db, filePath);
            var exception = await Assert.ThrowsAnyAsync<IOException>(() => Upload(service, movie.Id,
                Encode(SKEncodedImageFormat.Jpeg, 32, 24), "image/jpeg"));

            Assert.NotNull(exception);
            Assert.Null((await db.Movies.SingleAsync(m => m.Id == movie.Id)).PosterUrl);
        }
        finally { DeleteDirectory(parent); }
    }

    [Fact]
    public async Task Old_file_cleanup_failure_does_not_fail_poster_replacement()
    {
        var directory = NewDirectory();
        try
        {
            await using var db = database.CreateDbContext();
            var movie = await CreateMovie(db);
            var service = CreateService(db, directory);
            var oldUrl = await Upload(service, movie.Id, Encode(SKEncodedImageFormat.Jpeg, 32, 24), "image/jpeg");
            var oldId = Guid.Parse(oldUrl.Split('/').Last());
            var oldFile = Path.Combine(directory, $"{oldId:N}.jpg");
            File.Delete(oldFile);
            Directory.CreateDirectory(oldFile);

            var newUrl = await Upload(service, movie.Id, Encode(SKEncodedImageFormat.Jpeg, 40, 30), "image/jpeg");

            Assert.NotEqual(oldUrl, newUrl);
            Assert.Equal(newUrl, (await db.Movies.SingleAsync(m => m.Id == movie.Id)).PosterUrl);
            Assert.True(Directory.Exists(oldFile));
            Assert.True(File.Exists(Path.Combine(directory, $"{Guid.Parse(newUrl.Split('/').Last()):N}.jpg")));
        }
        finally { DeleteDirectory(directory); }
    }

    private static async Task<Movie> CreateMovie(LumiCine.Infrastructure.Persistence.LumiCineDbContext db)
    {
        var movie = new Movie(Guid.NewGuid(), $"Poster-{Guid.NewGuid():N}", 90, DateTime.UtcNow);
        db.Movies.Add(movie);
        await db.SaveChangesAsync();
        return movie;
    }

    private static LocalPosterService CreateService(LumiCine.Infrastructure.Persistence.LumiCineDbContext db, string directory)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:PostersPath"] = directory }).Build();
        return new LocalPosterService(db, TimeProvider.System, config, NullLogger<LocalPosterService>.Instance);
    }

    private static async Task<string> Upload(IPosterService service, Guid movieId, byte[] bytes, string contentType)
    {
        await using var stream = new MemoryStream(bytes);
        return await service.UploadAsync(movieId, stream, contentType, bytes.Length, default);
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 85)!;
        return data.ToArray();
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumicine-posters-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated database write failure.");
    }
}
