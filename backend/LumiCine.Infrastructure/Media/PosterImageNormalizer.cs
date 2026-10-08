using LumiCine.Application.Common;
using SkiaSharp;

namespace LumiCine.Infrastructure.Media;

public static class PosterImageNormalizer
{
    private const long MaxPixels = 20_000_000;
    private const int MaxEdge = 1600;

    public static byte[] Normalize(byte[] bytes, string contentType)
    {
        if (!string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(415, "Only JPEG and PNG posters are supported.");
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null ||
                (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) && codec.EncodedFormat != SKEncodedImageFormat.Jpeg) ||
                (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) && codec.EncodedFormat != SKEncodedImageFormat.Png))
                throw new ApiException(400, "Poster content does not match its JPEG or PNG media type.");

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
                throw new ApiException(400, "Poster dimensions cannot exceed 20 million pixels.");
            using var source = new SKBitmap(codec.Info);
            if (codec.GetPixels(source.Info, source.GetPixels()) != SKCodecResult.Success)
                throw new ApiException(400, "Poster image is corrupt or incomplete.");

            using var oriented = Orient(source, codec.EncodedOrigin);
            SKBitmap final = oriented;
            SKBitmap? resized = null;
            if (Math.Max(oriented.Width, oriented.Height) > MaxEdge)
            {
                var scale = (double)MaxEdge / Math.Max(oriented.Width, oriented.Height);
                var info = new SKImageInfo(Math.Max(1, (int)Math.Round(oriented.Width * scale)), Math.Max(1, (int)Math.Round(oriented.Height * scale)),
                    SKColorType.Rgba8888, SKAlphaType.Opaque);
                resized = oriented.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                if (resized is null) throw new ApiException(400, "Poster image could not be normalized.");
                final = resized;
            }

            try
            {
                using var image = SKImage.FromBitmap(final);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85)
                    ?? throw new ApiException(400, "Poster image could not be normalized.");
                return encoded.ToArray();
            }
            finally { resized?.Dispose(); }
        }
        catch (ApiException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ApiException(400, "Poster image is corrupt or could not be decoded.");
        }
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var output = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, source.Width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, source.Width, 0, -1, source.Height),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, source.Height),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, source.Height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, source.Height, -1, 0, source.Width),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, source.Width),
            _ => SKMatrix.CreateIdentity()
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        canvas.Flush();
        return output;
    }

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY) => new()
    {
        ScaleX = scaleX,
        SkewX = skewX,
        TransX = transX,
        SkewY = skewY,
        ScaleY = scaleY,
        TransY = transY,
        Persp2 = 1
    };
}
