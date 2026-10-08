namespace LumiCine.Application.Media;

public interface IPosterService
{
    Task<string> UploadAsync(Guid movieId, Stream content, string contentType, long length, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid movieId, CancellationToken cancellationToken);
    Task<PosterContent?> OpenAsync(Guid posterId, CancellationToken cancellationToken);
}

public sealed record PosterContent(Stream Content, string ContentType);
