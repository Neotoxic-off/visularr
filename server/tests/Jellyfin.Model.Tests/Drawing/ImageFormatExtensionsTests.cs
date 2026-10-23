using System;
using System.ComponentModel;
using MediaBrowser.Model.Drawing;
using Xunit;

namespace Jellyfin.Model.Drawing;

public static class ImageFormatExtensionsTests
{
    public static TheoryData<ImageFormat> GetAllImageFormats()
    {
        TheoryData<ImageFormat> theoryTypes = new TheoryData<ImageFormat>();
        foreach (ImageFormat x in Enum.GetValues<ImageFormat>())
        {
            theoryTypes.Add(x);
        }

        return theoryTypes;
    }

    [Theory]
    [MemberData(nameof(GetAllImageFormats))]
    public static void GetMimeType_Valid_Valid(ImageFormat format)
        => Assert.Null(Record.Exception(() => format.GetMimeType()));

    [Theory]
    [InlineData((ImageFormat)int.MinValue)]
    [InlineData((ImageFormat)int.MaxValue)]
    [InlineData((ImageFormat)(-1))]
    [InlineData((ImageFormat)6)]
    public static void GetMimeType_Valid_ThrowsInvalidEnumArgumentException(ImageFormat format)
        => Assert.Throws<InvalidEnumArgumentException>(() => format.GetMimeType());

    [Theory]
    [MemberData(nameof(GetAllImageFormats))]
    public static void GetExtension_Valid_Valid(ImageFormat format)
        => Assert.Null(Record.Exception(() => format.GetExtension()));

    [Theory]
    [InlineData((ImageFormat)int.MinValue)]
    [InlineData((ImageFormat)int.MaxValue)]
    [InlineData((ImageFormat)(-1))]
    [InlineData((ImageFormat)6)]
    public static void GetExtension_Valid_ThrowsInvalidEnumArgumentException(ImageFormat format)
        => Assert.Throws<InvalidEnumArgumentException>(() => format.GetExtension());
}
