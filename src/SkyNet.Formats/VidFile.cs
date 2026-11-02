using System.Collections.Immutable;
using System.Text;

namespace SkyNet.Formats;

/// <summary>
/// An XnGine movie (.VID: LOGO, OPENING, SEQ10…, Future Shock's BEGIN), read one frame at a time.
/// <code>
///   header  "VID\0" u8 2, u16 frames, u16 width, u16 height, u16 frame ticks, u16 ?   (15 bytes)
///   blocks  u8 type, then:
///     0x02  768 bytes of 6-bit RGB: the palette from here on (the first block; more may follow)
///     0x7C  u16 0, u8 Sound Blaster time constant (166: 11111 Hz), u16 n, n bytes 8-bit PCM
///     0x7D  u16 n, n bytes PCM (the next frame's sound)
///     0x03  u16 extra ticks, a whole frame          (RLE: n &lt; 0x80 literals; 0x80+n: n × next byte)
///     0x01  u16 extra ticks, changes from pixel 0   (RLE: n &lt; 0x80 literals; 0x80+n: skip n)
///     0x04  u16 extra ticks, u16 row, changes from that row (as 0x01)
///     0x00  nothing; 0x14 the end
/// </code>
/// RLE ends at a 0 byte or the frame's last pixel. A frame lasts (frame ticks + its extra ticks)
/// × 185 samples, its sound block's length exactly: the sound and the frames share one clock.
/// Daggerfall Unity's VidFile reads the same blocks but skips later palettes (OPENING has 25).
/// </summary>
public sealed class VidFile : IDisposable
{
    public const string Magic = "VID\0";
    /// <summary>Sound samples per tick (a frame of 4 ticks = 740 bytes at 11111 Hz, 15 fps).</summary>
    public const int TickSamples = 185;
    /// <summary>The Sound Blaster rate of time constant 166 (1 MHz / (256 - 166)); silent movies keep its clock.</summary>
    public const int SampleRate = 11111;

    private const int HeaderSize = 15;
    private const int PaletteBytes = 768;
    private const int TimeConstantBase = 256;
    private const int SoundBlasterClock = 1_000_000;
    private const int RunFlag = 0x80;
    // The largest picture the game shows (its menus' 320 × 200; the hi-res set's 640 × 480): a bigger header is no movie.
    private const int MaxWidth = 640;
    private const int MaxHeight = 480;
    // Sound Blaster rates a movie may name (time constant 166: 11111 Hz).
    private const int MinRate = 4000;
    private const int MaxRate = 48000;

    private enum Block : byte
    {
        Null = 0x00,
        Delta = 0x01,
        Palette = 0x02,
        Key = 0x03,
        DeltaFromRow = 0x04,
        End = 0x14,
        SoundStart = 0x7C,
        Sound = 0x7D,
    }

    // What 0x80 + n means: a run of one colour (a whole frame) or n pixels left as they were (changes).
    private enum Fill
    {
        Runs,
        Skips,
    }

    private enum Pass
    {
        Scan,
        Decode,
    }

    private readonly BinaryReader _reader;
    private readonly byte[] _pixels;
    private readonly long[] _startTicks;

    public int Width { get; }
    public int Height { get; }
    /// <summary>Frames in the file (the header's count, checked by a scan at open).</summary>
    public int FrameCount { get; }
    /// <summary>The whole movie's sound: 8-bit unsigned mono PCM at <see cref="AudioRate"/>; empty when silent.</summary>
    public ImmutableArray<byte> Audio { get; }
    /// <summary>The sound's sample rate; 0 when the movie is silent.</summary>
    public int AudioRate { get; }
    /// <summary>The movie's length in seconds (the sum of its frames').</summary>
    public double Seconds => _startTicks[^1] * (double)TickSamples / SampleRate;

    /// <summary>The frame decoded last; -1 before the first <see cref="Next"/>.</summary>
    public int Frame { get; private set; } = -1;
    /// <summary>The decoded frame: palette indices, <see cref="Width"/> × <see cref="Height"/>.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;
    /// <summary>The palette the decoded frame shows in (a new object when the movie changes it).</summary>
    public Palette Palette { get; private set; }

    private VidFile(BinaryReader reader, int width, int height, Palette palette, long[] startTicks, byte[] audio, int audioRate)
    {
        (_reader, Width, Height, Palette, _startTicks) = (reader, width, height, palette, startTicks);
        (Audio, AudioRate) = (ImmutableArray.Create(audio), audioRate);
        FrameCount = startTicks.Length - 1;
        _pixels = new byte[width * height];
    }

    /// <summary>Opens a movie (the stream is owned from here on): the header read and the blocks
    /// scanned once for the sound and the timing; frames are decoded later by <see cref="Next"/>.
    /// Null when it is not a VID or its blocks do not parse.</summary>
    public static VidFile? Open(Stream stream)
    {
        var reader = new BinaryReader(new BufferedStream(stream, 1 << 16));
        try
        {
            var magic = Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length));
            reader.ReadByte();
            var frames = reader.ReadUInt16();
            var (width, height) = (reader.ReadUInt16(), reader.ReadUInt16());
            var ticks = reader.ReadUInt16();
            reader.ReadUInt16();
            if (magic != Magic || width is 0 or > MaxWidth || height is 0 or > MaxHeight || (Block)reader.ReadByte() != Block.Palette)
            {
                reader.Dispose();
                return null;
            }

            var palette = ReadPalette(reader);
            var (starts, audio, rate) = Scan(reader, width, height, ticks);
            if (starts.Length - 1 != frames)
            {
                reader.Dispose();
                return null;
            }

            reader.BaseStream.Position = HeaderSize;
            return new VidFile(reader, width, height, palette, starts, audio, rate);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException)
        {
            reader.Dispose();
            return null;
        }
    }

    /// <summary>The time frame <paramref name="frame"/> shows from, in seconds (<see cref="FrameCount"/>: the end).</summary>
    public double FrameTime(int frame) => _startTicks[Math.Clamp(frame, 0, FrameCount)] * (double)TickSamples / SampleRate;

    /// <summary>Decodes the next frame (applying the palettes before it); false past the last one.</summary>
    public bool Next()
    {
        if (Frame + 1 >= FrameCount)
        {
            return false;
        }

        while (true)
        {
            var block = (Block)_reader.ReadByte();
            if (block == Block.Palette)
            {
                Palette = ReadPalette(_reader);
                continue;
            }

            if (Video(block, _reader, _pixels, Width, Pass.Decode) == null)
            {
                Skip(block, _reader);
                continue;
            }

            Frame++;
            return true;
        }
    }

    public void Dispose() => _reader.Dispose();

    // The block walk once: each frame's start tick (the last entry is the end) and the sound.
    private static (long[] Starts, byte[] Audio, int Rate) Scan(BinaryReader r, int width, int height, int frameTicks)
    {
        var starts = new List<long> { 0 };
        var audio = new MemoryStream();
        var rate = 0;
        var scratch = new byte[width * height];
        while (true)
        {
            var block = (Block)r.ReadByte();
            if (block == Block.End)
            {
                return ([.. starts], audio.ToArray(), rate);
            }

            if (block == Block.Palette)
            {
                r.BaseStream.Seek(PaletteBytes, SeekOrigin.Current);
                continue;
            }

            if (block == Block.SoundStart)
            {
                r.ReadUInt16();
                rate = SoundBlasterClock / (TimeConstantBase - r.ReadByte());
                if (rate is < MinRate or > MaxRate)
                {
                    throw new InvalidDataException($"VID sound at {rate} Hz");
                }

                audio.Write(r.ReadBytes(r.ReadUInt16()));
                continue;
            }

            if (block == Block.Sound)
            {
                audio.Write(r.ReadBytes(r.ReadUInt16()));
                continue;
            }

            if (Video(block, r, scratch, width, Pass.Scan) is { } extra)
            {
                starts.Add(starts[^1] + frameTicks + extra);
                continue;
            }

            Skip(block, r);
        }
    }

    // A frame block decoded into pixels (Scan: only walked); its extra ticks, null for a block that is no frame.
    private static int? Video(Block block, BinaryReader r, byte[] pixels, int width, Pass pass)
    {
        if (block is not (Block.Key or Block.Delta or Block.DeltaFromRow))
        {
            return null;
        }

        int extra = r.ReadUInt16();
        var start = block == Block.DeltaFromRow ? r.ReadUInt16() * width : 0;
        if (start > pixels.Length)
        {
            throw new InvalidDataException("VID row past the frame");
        }

        Rle(r, pixels, start, block == Block.Key ? Fill.Runs : Fill.Skips, pass);
        return extra;
    }

    // RLE into pixels from pos: n < 0x80 copies n bytes; 0x80 + n repeats the next byte n times
    // (a whole frame) or skips n pixels (changes); 0 or the last pixel ends it.
    private static void Rle(BinaryReader r, byte[] pixels, int pos, Fill fill, Pass pass)
    {
        while (pos < pixels.Length)
        {
            int code = r.ReadByte();
            if (code == 0)
            {
                return;
            }

            if (code < RunFlag)
            {
                if (code > pixels.Length - pos)
                {
                    throw new InvalidDataException("VID literal run past the frame");
                }

                r.BaseStream.ReadExactly(pixels, pos, code);
                pos += code;
                continue;
            }

            var count = Math.Min(code - RunFlag, pixels.Length - pos);
            if (fill == Fill.Runs)
            {
                var colour = r.ReadByte();
                if (pass == Pass.Decode)
                {
                    pixels.AsSpan(pos, count).Fill(colour);
                }
            }

            pos += count;
        }
    }

    // Past a block that is no frame and no palette: the sound (read at open) or nothing.
    private static void Skip(Block block, BinaryReader r)
    {
        if (block == Block.SoundStart)
        {
            r.ReadBytes(sizeof(ushort) + sizeof(byte));
        }

        if (block is Block.SoundStart or Block.Sound)
        {
            r.BaseStream.Seek(r.ReadUInt16(), SeekOrigin.Current);
            return;
        }

        if (block != Block.Null)
        {
            throw new InvalidDataException($"VID block 0x{(byte)block:X2}");
        }
    }

    private static Palette ReadPalette(BinaryReader r) =>
        Palette.Parse(r.ReadBytes(PaletteBytes)) ?? throw new EndOfStreamException();
}
