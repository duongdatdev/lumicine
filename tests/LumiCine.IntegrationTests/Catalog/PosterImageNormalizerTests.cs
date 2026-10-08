using LumiCine.Application.Common;
using LumiCine.Infrastructure.Media;
using SkiaSharp;

namespace LumiCine.IntegrationTests.Catalog;

public sealed class PosterImageNormalizerTests
{
    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 80, 60, "Red", "Green", "Blue", "Yellow")]
    [InlineData(SKEncodedOrigin.TopRight, 80, 60, "Green", "Red", "Yellow", "Blue")]
    [InlineData(SKEncodedOrigin.BottomRight, 80, 60, "Yellow", "Blue", "Green", "Red")]
    [InlineData(SKEncodedOrigin.BottomLeft, 80, 60, "Blue", "Yellow", "Red", "Green")]
    [InlineData(SKEncodedOrigin.LeftTop, 60, 80, "Red", "Blue", "Green", "Yellow")]
    [InlineData(SKEncodedOrigin.RightTop, 60, 80, "Blue", "Red", "Yellow", "Green")]
    [InlineData(SKEncodedOrigin.RightBottom, 60, 80, "Yellow", "Green", "Blue", "Red")]
    [InlineData(SKEncodedOrigin.LeftBottom, 60, 80, "Green", "Yellow", "Red", "Blue")]
    public void Normalize_applies_each_exif_orientation_and_removes_metadata(SKEncodedOrigin origin, int expectedWidth,
        int expectedHeight, string topLeft, string topRight, string bottomLeft, string bottomRight)
    {
        var input = AddExifOrientation(EncodeQuadrants(), (ushort)origin);
        var normalized = PosterImageNormalizer.Normalize(input, "image/jpeg");

        using var data = SKData.CreateCopy(normalized);
        using var codec = SKCodec.Create(data);
        using var bitmap = SKBitmap.Decode(data);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec!.EncodedFormat);
        Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        Assert.Equal(expectedWidth, bitmap!.Width);
        Assert.Equal(expectedHeight, bitmap.Height);
        AssertColor(Color(topLeft), bitmap.GetPixel(bitmap.Width / 4, bitmap.Height / 4));
        AssertColor(Color(topRight), bitmap.GetPixel(bitmap.Width * 3 / 4, bitmap.Height / 4));
        AssertColor(Color(bottomLeft), bitmap.GetPixel(bitmap.Width / 4, bitmap.Height * 3 / 4));
        AssertColor(Color(bottomRight), bitmap.GetPixel(bitmap.Width * 3 / 4, bitmap.Height * 3 / 4));
    }

    [Fact]
    public void Normalize_rejects_fake_malformed_mismatched_and_oversized_images()
    {
        Assert.Equal(400, Assert.Throws<ApiException>(() => PosterImageNormalizer.Normalize([1, 2, 3], "image/jpeg")).StatusCode);
        Assert.Equal(400, Assert.Throws<ApiException>(() => PosterImageNormalizer.Normalize(Encode(SKEncodedImageFormat.Png, 20, 20), "image/jpeg")).StatusCode);
        Assert.Equal(400, Assert.Throws<ApiException>(() => PosterImageNormalizer.Normalize(Encode(SKEncodedImageFormat.Jpeg, 5001, 4000), "image/jpeg")).StatusCode);
        Assert.Equal(415, Assert.Throws<ApiException>(() => PosterImageNormalizer.Normalize([1, 2, 3], "image/svg+xml")).StatusCode);
    }

    private static byte[] EncodeQuadrants()
    {
        using var bitmap = new SKBitmap(80, 60);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint();
            canvas.Clear(SKColors.Black);
            paint.Color = SKColors.Red;
            canvas.DrawRect(new SKRect(0, 0, 40, 30), paint);
            paint.Color = SKColors.Green;
            canvas.DrawRect(new SKRect(40, 0, 80, 30), paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(new SKRect(0, 30, 40, 60), paint);
            paint.Color = SKColors.Yellow;
            canvas.DrawRect(new SKRect(40, 30, 80, 60), paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100)!;
        return data.ToArray();
    }

    private static byte[] AddExifOrientation(byte[] jpeg, ushort orientation)
    {
        var payload = new byte[32];
        "Exif\0\0"u8.CopyTo(payload);
        "II"u8.CopyTo(payload.AsSpan(6));
        payload[8] = 42;
        payload[10] = 8;
        payload[14] = 1;
        payload[16] = 0x12;
        payload[17] = 0x01;
        payload[18] = 3;
        payload[20] = 1;
        payload[24] = (byte)orientation;
        payload[25] = (byte)(orientation >> 8);
        var segment = new byte[payload.Length + 4];
        segment[0] = 0xff;
        segment[1] = 0xe1;
        segment[2] = 0;
        segment[3] = (byte)(payload.Length + 2);
        payload.CopyTo(segment, 4);
        return [.. jpeg.AsSpan(0, 2).ToArray(), .. segment, .. jpeg.AsSpan(2).ToArray()];
    }

    private static SKColor Color(string name) => name switch
    {
        "Red" => SKColors.Red,
        "Green" => SKColors.Green,
        "Blue" => SKColors.Blue,
        _ => SKColors.Yellow
    };

    private static void AssertColor(SKColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 35);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 35);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 35);
    }

    private static byte[] Encode(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 85)!;
        return data.ToArray();
    }
}
