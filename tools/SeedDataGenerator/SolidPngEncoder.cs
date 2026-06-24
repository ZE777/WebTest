using System.IO.Compression;
using System.Text;

namespace SeedDataGenerator;

/// <summary>
/// 極簡、<b>無第三方相依</b>的 PNG 編碼器(僅用 BCL:<see cref="ZLibStream"/> + 自寫 CRC32)。
/// 提供兩種測試用設備圖(二位元圖檔來源):純色塊,或「工業漸層 + 振動波形」卡片圖。
/// </summary>
public static class SolidPngEncoder
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>產生指定尺寸的單色 PNG(RGB)。</summary>
    public static byte[] CreateSolid(int width, int height, byte r, byte g, byte b)
    {
        var raw = new byte[height * (1 + width * 3)];
        var pos = 0;
        for (var y = 0; y < height; y++)
        {
            raw[pos++] = 0; // filter: None
            for (var x = 0; x < width; x++) { raw[pos++] = r; raw[pos++] = g; raw[pos++] = b; }
        }
        return Encode(raw, width, height);
    }

    /// <summary>
    /// 產生「設備卡片圖」:深藍工業<b>垂直漸層</b>底 + 淡格線 + 一條<b>振動波形</b>(主題圖騰,
    /// 用主色青系)。以 <paramref name="variant"/> 微調頻率/振幅/相位與色相,讓每台略有差異。
    /// 刻意不帶綠/黃/紅(狀態語意色保留給狀態燈,見 docs/05)。
    /// </summary>
    public static byte[] CreateMachineCard(int width, int height, int variant)
    {
        // 底色漸層:上深下淺(工業深藍)
        var top = (r: 12, g: 20, b: 34);     // #0C1422
        var bot = (r: 26, g: 42, b: 68);     // #1A2A44
        // 波形色(青系,主色家族;隨 variant 換一個近似色相)
        var accents = new (int r, int g, int b)[]
        {
            (34, 211, 238), (45, 196, 222), (60, 205, 212),
            (40, 182, 235), (90, 210, 222), (34, 176, 205)
        };
        var acc = accents[variant % accents.Length];

        var freq = 2.0 + (variant % 3) * 0.8;          // 波數
        var amp = height * (0.11 + (variant % 4) * 0.022);
        var phase = (variant % 5) * 0.85;
        var midY = height * 0.60;
        const double lineW = 2.4;

        var raw = new byte[height * (1 + width * 3)];
        var pos = 0;
        for (var y = 0; y < height; y++)
        {
            raw[pos++] = 0; // filter: None
            var t = (double)y / (height - 1);
            var br = (int)(top.r + (bot.r - top.r) * t);
            var bg = (int)(top.g + (bot.g - top.g) * t);
            var bb = (int)(top.b + (bot.b - top.b) * t);

            for (var x = 0; x < width; x++)
            {
                int r = br, g = bg, b = bb;

                // 淡垂直格線(每 40px)
                if (x % 40 == 0) { r += 7; g += 9; b += 12; }

                // 波形:像素到該 x 波形 y 的距離 → 在線寬內混入主色(近似抗鋸齒)
                var wy = midY + amp * Math.Sin((double)x / width * Math.PI * 2 * freq + phase);
                var d = Math.Abs(y - wy);
                if (d < lineW)
                {
                    var a = 1.0 - d / lineW;
                    r = Blend(r, acc.r, a); g = Blend(g, acc.g, a); b = Blend(b, acc.b, a);
                }
                else if (y > wy && y - wy < amp * 1.3)
                {
                    // 波形下方極淡輝光帶
                    var a = 0.07 * (1 - (y - wy) / (amp * 1.3));
                    r = Blend(r, acc.r, a); g = Blend(g, acc.g, a); b = Blend(b, acc.b, a);
                }

                raw[pos++] = Clamp(r); raw[pos++] = Clamp(g); raw[pos++] = Clamp(b);
            }
        }
        return Encode(raw, width, height);
    }

    private static int Blend(int from, int to, double a) => (int)(from + (to - from) * a);
    private static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    /// <summary>把 truecolor 掃描列(含 filter byte)壓成 PNG。</summary>
    private static byte[] Encode(byte[] raw, int width, int height)
    {
        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
            idat = ms.ToArray();
        }

        using var outMs = new MemoryStream();
        outMs.Write(Signature);

        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type: truecolor (RGB)
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(outMs, "IHDR", ihdr);
        WriteChunk(outMs, "IDAT", idat);
        WriteChunk(outMs, "IEND", Array.Empty<byte>());
        return outMs.ToArray();
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var lengthBuffer = new byte[4];
        WriteBigEndian(lengthBuffer, 0, (uint)data.Length);
        stream.Write(lengthBuffer);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcBuffer = new byte[4];
        WriteBigEndian(crcBuffer, 0, Crc32.Compute(typeBytes, data));
        stream.Write(crcBuffer);
    }
}

/// <summary>標準 CRC-32(PNG 用,多項式 0xEDB88320)。</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Compute(params byte[][] segments)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var segment in segments)
            foreach (var b in segment)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
