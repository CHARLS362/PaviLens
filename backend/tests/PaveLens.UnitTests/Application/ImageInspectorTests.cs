using PaveLens.Application.Images;

namespace PaveLens.UnitTests.Application;

public class ImageInspectorTests
{
    [Fact]
    public void ReadsJpegDimensionsFromTheSample()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", "av-el-sol-bache.jpg"));

        var info = ImageInspector.Inspect(bytes);

        Assert.NotNull(info);
        Assert.Equal("image/jpeg", info.ContentType);
        Assert.Equal(".jpg", info.Extension);
        Assert.True(info.Width > 0 && info.Height > 0);
    }

    [Fact]
    public void ReadsPngHeader()
    {
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0, 0, 0x02, 0x80, 0, 0, 0x01, 0xE0, 8, 2, 0, 0, 0
        ];

        var info = ImageInspector.Inspect(png);

        Assert.NotNull(info);
        Assert.Equal((640, 480), (info.Width, info.Height));
    }

    [Fact]
    public void RejectsBytesThatAreNotAnImage()
    {
        Assert.Null(ImageInspector.Inspect("GIF89a no soportado"u8));
        Assert.Null(ImageInspector.Inspect([]));
    }
}
