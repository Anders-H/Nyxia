using System.Drawing.Imaging;
using System.Text;

namespace ImageConverter;

internal static class CbmPrgStudioExporter
{
    private const int Width = 96, Height = 42;
    private const int ImageX = 112, ImageY = 16;
    private const int BitmapRaster = 48;
    // With YSCROLL=3, the first visible bitmap line is raster 51.
    // Sprite Y registers use 50 + image Y; their first pixels appear one line later.
    private const int TextRaster = 51 + ImageY + Height;

    public static void Save(string pngFileSource, string asmFileTarget)
    {
        using var image = new Bitmap(pngFileSource);
        
        if (image.RawFormat.Guid != ImageFormat.Png.Guid || image.Width != Width || image.Height != Height)
            throw new ArgumentException("Input must be a 96x42 PNG.", nameof(pngFileSource));

        var pixels = new int[Width * Height];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var color = image.GetPixel(x, y);
                var rgb = color.ToArgb() & 0xffffff;

                if (color.A != 255 || rgb is not (0x444444 or 0x588d43 or 0x9ad284))
                    throw new ArgumentException($"Invalid pixel ({x}, {y}): #{color.ToArgb():X8}. Expected opaque #444444, #588D43 or #9AD284.", nameof(pngFileSource));

                pixels[y * Width + x] = rgb;
            }
        }

        // Standard hires bitmap: 40x25 cells, eight consecutive bytes per cell.
        // Include the entire 8 KiB bitmap block. Its unused final 2 KiB
        // ($3800-$3FFF) also supplies 256 completely blank text characters.
        var bitmap = new byte[8192];
        var screen = Enumerable.Repeat((byte)0x66, 1000).ToArray();
        var sprites = new byte[8 * 64];
        // Six storage rows remain, but the raster split hides the final six
        // pixel rows by switching to blank text immediately below the image.
        for (var cy = 2; cy < 8; cy++)
        {
            for (var cx = 14; cx < 26; cx++)
            {
                screen[cy * 40 + cx] = 0x5b; // Set bit: green (5); clear: dark grey (11).
            }
        }

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var color = pixels[y * Width + x];

                if (color == 0x588d43)
                {
                    var sx = ImageX + x;
                    var sy = ImageY + y;
                    bitmap[(sy / 8 * 40 + sx / 8) * 8 + sy % 8] |= (byte)(0x80 >> (sx % 8));
                }
                else if (color == 0x9ad284)
                {
                    var sprite = y / 21 * 4 + x / 24;
                    sprites[sprite * 64 + y % 21 * 3 + x % 24 / 8] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        var asm = new StringBuilder();
        asm.AppendLine("* = $0801");
        asm.AppendLine("        BYTE $0B,$08,$0A,$00,$9E,$34,$30,$39,$36,$00,$00,$00");
        asm.AppendLine("* = $0C00");
        AppendBytes(asm, "SpriteData", sprites);
        asm.AppendLine("* = $1000");
        asm.AppendLine("Start");
        asm.AppendLine("        sei");
        asm.AppendLine("        cld");
        asm.AppendLine("        lda #$2F");
        asm.AppendLine("        sta $00 ; explicitly configure the 6510 memory-mapping outputs");
        asm.AppendLine("        lda #$35");
        asm.AppendLine("        sta $01 ; RAM at BASIC/KERNAL, I/O visible; no ROM interrupt handlers");
        asm.AppendLine("        lda #<NmiHandler");
        asm.AppendLine("        sta $FFFA");
        asm.AppendLine("        lda #>NmiHandler");
        asm.AppendLine("        sta $FFFB");
        asm.AppendLine("        lda #$7F");
        asm.AppendLine("        sta $DC0D");
        asm.AppendLine("        sta $DD0D");
        asm.AppendLine("        lda $DC0D");
        asm.AppendLine("        lda $DD0D");
        asm.AppendLine("        lda #$00");
        asm.AppendLine("        sta $D01A");
        asm.AppendLine("        sta $D015");
        asm.AppendLine("        sta $D011 ; blank screen while setting up");
        asm.AppendLine("        lda $DD02");
        asm.AppendLine("        ora #$03");
        asm.AppendLine("        sta $DD02");
        asm.AppendLine("        lda $DD00");
        asm.AppendLine("        ora #$03");
        asm.AppendLine("        sta $DD00 ; VIC bank $0000-$3FFF");
        asm.AppendLine("        lda #$06");
        asm.AppendLine("        sta $D020");
        asm.AppendLine("        sta $D021");
        asm.AppendLine("        lda #$08");
        asm.AppendLine("        sta $D016 ; hires, 40 columns, no horizontal scroll");
        asm.AppendLine("        lda #$1E");
        asm.AppendLine("        sta $D018 ; screen $0400; bitmap $2000 OR blank charset $3800");
        asm.AppendLine("        ldx #$00");
        asm.AppendLine("CopyScreen");

        for (var page = 0; page < 3; page++)
        {
            asm.AppendLine($"        lda ScreenData+${page * 256:X4},x");
            asm.AppendLine($"        sta ${0x0400 + page * 256:X4},x");
        }
        
        asm.AppendLine("        inx");
        asm.AppendLine("        bne CopyScreen");
        asm.AppendLine("        ldx #$00");
        asm.AppendLine("CopyScreenTail");
        asm.AppendLine("        lda ScreenData+$0300,x");
        asm.AppendLine("        sta $0700,x");
        asm.AppendLine("        inx");
        asm.AppendLine("        cpx #$E8");
        asm.AppendLine("        bne CopyScreenTail");
        asm.AppendLine("        lda #$00");
        
        foreach (var register in new[] { 0xd010, 0xd017, 0xd01b, 0xd01c, 0xd01d })
            asm.AppendLine($"        sta ${register:X4}");

        for (var i = 0; i < 8; i++)
        {
            asm.AppendLine($"        lda #${0x30 + i:X2}");
            asm.AppendLine($"        sta ${0x07f8 + i:X4} ; sprite {i} pointer");
            asm.AppendLine($"        lda #${24 + ImageX + i % 4 * 24:X2}");
            asm.AppendLine($"        sta ${0xd000 + i * 2:X4}");
            asm.AppendLine($"        lda #${50 + ImageY + i / 4 * 21:X2}");
            asm.AppendLine($"        sta ${0xd001 + i * 2:X4}");
            asm.AppendLine("        lda #$0D");
            asm.AppendLine($"        sta ${0xd027 + i:X4}");
        }

        asm.AppendLine("        lda #$FF");
        asm.AppendLine("        sta $D015");
        asm.AppendLine("        lda #<BitmapIrq");
        asm.AppendLine("        sta $FFFE");
        asm.AppendLine("        lda #>BitmapIrq");
        asm.AppendLine("        sta $FFFF");
        asm.AppendLine($"        lda #${BitmapRaster:X2}");
        asm.AppendLine("        sta $D012");
        asm.AppendLine("        lda #$1B ; start in blank text; raster compare bit 8 is zero");
        asm.AppendLine("        sta $D011");
        asm.AppendLine("        lda #$0F");
        asm.AppendLine("        sta $D019 ; clear pending VIC interrupts before enabling raster IRQ");
        asm.AppendLine("        lda #$01");
        asm.AppendLine("        sta $D01A");
        asm.AppendLine("        cli");
        asm.AppendLine("Forever");
        asm.AppendLine("        jmp Forever");
        asm.AppendLine("BitmapIrq");
        asm.AppendLine("        pha ; X and Y are never modified by either IRQ handler");
        asm.AppendLine("        lda #$3B ; bitmap mode before the top of the visible screen");
        asm.AppendLine("        sta $D011");
        asm.AppendLine($"        lda #${TextRaster:X2}");
        asm.AppendLine("        sta $D012");
        asm.AppendLine("        lda #<TextIrq");
        asm.AppendLine("        sta $FFFE");
        asm.AppendLine("        lda #>TextIrq");
        asm.AppendLine("        sta $FFFF");
        asm.AppendLine("        lda #$01");
        asm.AppendLine("        sta $D019");
        asm.AppendLine("        pla");
        asm.AppendLine("        rti");
        asm.AppendLine("TextIrq");
        asm.AppendLine("        pha");
        asm.AppendLine("        lda #$1B ; switch first: blue text starts before image X=112");
        asm.AppendLine("        sta $D011 ; same scroll/DEN/RSEL, only bitmap bit changes");
        asm.AppendLine($"        lda #${BitmapRaster:X2}");
        asm.AppendLine("        sta $D012");
        asm.AppendLine("        lda #<BitmapIrq");
        asm.AppendLine("        sta $FFFE");
        asm.AppendLine("        lda #>BitmapIrq");
        asm.AppendLine("        sta $FFFF");
        asm.AppendLine("        lda #$01");
        asm.AppendLine("        sta $D019");
        asm.AppendLine("        pla");
        asm.AppendLine("        rti");
        asm.AppendLine("NmiHandler");
        asm.AppendLine("        rti");
        asm.AppendLine("* = $2000");
        AppendBytes(asm, "BitmapData", bitmap);
        asm.AppendLine("* = $4000");
        AppendBytes(asm, "ScreenData", screen);

        // Complete validation and data construction before touching the destination.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(asmFileTarget))!);
        File.WriteAllText(asmFileTarget, asm.ToString(), Encoding.ASCII);
        Console.WriteLine($"{Path.GetFileName(pngFileSource)} -> {asmFileTarget}");
    }

    private static void AppendBytes(StringBuilder asm, string label, byte[] data)
    {
        asm.AppendLine(label);

        for (var i = 0; i < data.Length; i += 16)
            asm.AppendLine("        BYTE " + string.Join(",", data.Skip(i).Take(16).Select(b => $"${b:X2}")));
    }
}
