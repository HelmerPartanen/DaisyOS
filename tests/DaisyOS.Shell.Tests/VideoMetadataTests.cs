using DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class VideoMetadataTests
{
    [Fact]
    public void TenBitP3GammaVideoIsSdrAndKeepsHighFrameRate()
    {
        var metadata = VideoMetadata.Parse("""
            {"streams":[{"width":3840,"height":2160,"pix_fmt":"yuv420p10le",
            "avg_frame_rate":"240/1","duration":"120.008333","color_primaries":"smpte432",
            "color_transfer":"bt470m","color_space":"bt709","color_range":"tv"}]}
            """);
        Assert.False(metadata.IsHdr);
        Assert.Equal(240, metadata.Fps);
        Assert.Equal("smpte432", metadata.Primaries);
        Assert.Equal((1920, 1080), VideoWallpaperOptimization.CoverSize(metadata, 2560, 1440));
        Assert.True(VideoWallpaperOptimization.RequiresOptimizedCopy(metadata, 1920, 1080));
    }

    [Theory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    public void HdrTransferIsExplicitAndMasteringMetadataSurvivesParsing(string transfer)
    {
        var json = """
            {"streams":[{"width":1920,"height":1080,"avg_frame_rate":"60000/1001","color_transfer":"TRANSFER"}],
            "frames":[{"side_data_list":[{"side_data_type":"Mastering display metadata",
            "red_x":"17/25","red_y":"8/25","green_x":"53/200","green_y":"69/100",
            "blue_x":"3/20","blue_y":"3/50","white_point_x":"3127/10000","white_point_y":"329/1000",
            "max_luminance":"1000/1","min_luminance":"1/200"},
            {"side_data_type":"Content light level metadata","max_content":1200,"max_average":400}]}]}
            """.Replace("TRANSFER", transfer);
        var metadata = VideoMetadata.Parse(json);
        Assert.True(metadata.IsHdr);
        Assert.Equal(1000, metadata.PeakNits);
        Assert.Equal(1200, metadata.MaxCll);
        Assert.Equal(400, metadata.MaxFall);
        Assert.Equal(50, metadata.MinimumLuminance);
        Assert.Equal(680000, metadata.MasteringPrimaries[0]);
        Assert.InRange(metadata.Fps, 59.93, 59.95);
    }

    [Fact]
    public void CoverSizingPreservesAspectRotationAndNeverUpscales()
    {
        var source = new VideoMetadata(1920,1080,30,1,"","","","",0,1000,0,0,0,new int[8]);
        Assert.Equal((1920,1080), VideoWallpaperOptimization.CoverSize(source,3840,2160));
        Assert.Equal((178,100), VideoWallpaperOptimization.CoverSize(source,100,100));
        Assert.Equal((1920,1080), VideoWallpaperOptimization.CoverSize(source with { Rotation=90 },1920,1080));
        Assert.False(VideoWallpaperOptimization.RequiresOptimizedCopy(source, 1920, 1080));
    }
}
