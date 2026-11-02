namespace SkyNet.Formats.Tests;

public class VidFileTests
{
    // A 4 × 2 movie: palette, sound, a whole frame, a sound block, a change from row 1, the end.
    static byte[] Movie() => new Bytes()
        .Raw("VID\0"u8).U8(2).U16(2).U16(4).U16(2).U16(4).U16(14)
        .U8(0x02).Raw(Enumerable.Range(0, 768).Select(i => (byte)(i % 64)).ToArray())
        .U8(0x7C).U16(0).U8(166).U16(740).Zeros(740)
        .U8(0x03).U16(0).U8(0x83).U8(7).U8(2).U8(1).U8(2).U8(0x82).U8(9)
        .U8(0x00)
        .U8(0x7D).U16(925).Zeros(925)
        .U8(0x04).U16(1).U16(1).U8(0x81).U8(1).U8(5).U8(0)
        .U8(0x14)
        .ToArray();

    [Fact]
    public void Decodes_runs_skips_and_rows()
    {
        using var vid = VidFile.Open(new MemoryStream(Movie()))!;
        Assert.Equal((4, 2, 2), (vid.Width, vid.Height, vid.FrameCount));

        Assert.True(vid.Next());
        Assert.Equal([7, 7, 7, 1, 2, 9, 9, 0], vid.Pixels.ToArray());

        Assert.True(vid.Next());
        Assert.Equal([7, 7, 7, 1, 2, 5, 9, 0], vid.Pixels.ToArray());
        Assert.False(vid.Next());
    }

    [Fact]
    public void Frames_and_sound_share_one_clock()
    {
        using var vid = VidFile.Open(new MemoryStream(Movie()))!;
        Assert.Equal(VidFile.SampleRate, vid.AudioRate);
        Assert.Equal(740 + 925, vid.Audio.Length);
        Assert.Equal(740.0 / VidFile.SampleRate, vid.FrameTime(1), 9);
        Assert.Equal(vid.Audio.Length / (double)vid.AudioRate, vid.Seconds, 9);
    }

    [Fact]
    public void Not_a_vid_is_null()
    {
        Assert.Null(VidFile.Open(new MemoryStream("VOC\0xxxxxxxxxxxxxxxxxx"u8.ToArray())));
        Assert.Null(VidFile.Open(new MemoryStream(Movie()[..900])));
    }

    // A header of 65535 × 65535 (4 GB a frame) is refused, not allocated.
    [Fact]
    public void Huge_size_is_null()
    {
        var movie = Movie();
        movie[7] = movie[8] = movie[9] = movie[10] = 0xFF;
        Assert.Null(VidFile.Open(new MemoryStream(movie)));
    }

    [Fact]
    public void Row_past_the_frame_is_null()
    {
        // One 4 × 2 frame of changes from row 3 (row 2, the end, is an empty change: LOGO has them).
        var movie = new Bytes()
            .Raw("VID\0"u8).U8(2).U16(1).U16(4).U16(2).U16(4).U16(14)
            .U8(0x02).Zeros(768)
            .U8(0x04).U16(0).U16(3)
            .U8(0x14)
            .ToArray();
        Assert.Null(VidFile.Open(new MemoryStream(movie)));
    }

    // Time constant 255: 1 MHz, no sound card's rate.
    [Fact]
    public void Sound_rate_out_of_range_is_null()
    {
        var movie = Movie();
        movie[16 + 768 + 3] = 255;
        Assert.Null(VidFile.Open(new MemoryStream(movie)));
    }

    // Name, frames, height, sound (Hz), a frame and the MD5 of its indices (an independent decoder's).
    public static TheoryData<string, int, int, int, int, string> Real => new()
    {
        { "LOGO.VID", 64, 200, 11111, 40, "626f06901876f14fdd0440e4ee0b0209" },
        { "OPENING.VID", 843, 200, 11111, 400, "4c3142f82818d4dcbd219a963f9396fc" },
        { "SEQ10.VID", 2111, 186, 11111, 1000, "fbd85997e11a299949d8f468c81053c1" },
        { "BEGIN.VID", 553, 200, 0, 300, "9f26adc2887e0f54910fdee0aa130426" },
    };

    [DataTheory]
    [MemberData(nameof(Real))]
    public void Real_movies_decode(string name, int frames, int height, int rate, int frame, string md5)
    {
        var gd = TestData.Open();
        using var vid = VidFile.Open(File.OpenRead(gd.PathOf(name)))!;
        Assert.Equal((320, height, frames, rate), (vid.Width, vid.Height, vid.FrameCount, vid.AudioRate));

        var first = vid.Palette;
        while (vid.Frame < frame)
        {
            Assert.True(vid.Next());
        }

        Assert.Equal(md5, TestData.Md5(vid.Pixels));
        if (name == "OPENING.VID")
        {
            Assert.NotSame(first, vid.Palette);
        }

        if (rate > 0)
        {
            Assert.InRange(vid.Audio.Length / (double)rate - vid.Seconds, -1.0, 0.1);
        }
    }

    [DataFact]
    public void Every_movie_opens_and_its_sound_fits_its_frames()
    {
        var gd = TestData.Open();
        var names = Directory.GetFiles(gd.Dir, "*.VID", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });
        Assert.True(names.Length >= 19, $"{names.Length} VIDs");
        var exact = 0;
        foreach (var path in names)
        {
            using var vid = VidFile.Open(File.OpenRead(path));
            Assert.NotNull(vid);
            if (vid.AudioRate == 0)
            {
                continue;
            }

            // The sound never outlasts the pictures; most end together, a few end silent
            // (SCENE3's last 69 frames, 4.6 s; SEQ10's last 11).
            var diff = vid.Audio.Length / (double)vid.AudioRate - vid.Seconds;
            Assert.InRange(diff, -5.0, 0.001);
            exact += diff > -0.001 ? 1 : 0;
        }

        Assert.True(exact >= 10, $"{exact} movies end with their sound");
    }
}

/// <summary>A theory that is skipped unless SKYNET_DATA points at SkyNET's data folder.</summary>
sealed class DataTheoryAttribute : TheoryAttribute
{
    public DataTheoryAttribute()
    {
        if (TestData.Dir == null)
            Skip = $"{TestData.EnvVar} is not set to a SkyNET data folder";
    }
}
