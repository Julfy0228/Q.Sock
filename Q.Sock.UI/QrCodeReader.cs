using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Q.Sock.UI;

public static class QrCodeReader
{
    public static string Decode(Stream image)
    {
        using var codec =
            SKCodec.Create(image)
            ?? throw new InvalidDataException("Не удалось прочитать изображение.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > 12_000_000)
            throw new InvalidDataException(
                "Изображение должно содержать не более 12 мегапикселей."
            );
        using var bitmap =
            SKBitmap.Decode(
                codec,
                new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Opaque)
            ) ?? throw new InvalidDataException("Не удалось декодировать изображение.");
        var source = new RGBLuminanceSource(
            bitmap.Bytes,
            bitmap.Width,
            bitmap.Height,
            RGBLuminanceSource.BitmapFormat.BGRA32
        );
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = [BarcodeFormat.QR_CODE],
            },
        };
        return reader.Decode(source)?.Text
            ?? throw new InvalidDataException("QR-код на изображении не найден.");
    }
}

public interface IQrScanner
{
    Task<string?> Scan();
}
