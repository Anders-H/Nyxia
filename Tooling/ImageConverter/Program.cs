using System.Drawing.Imaging;
using ImageConverter;

// Resolve folders relative to the application, independently of the working directory.
var toolingFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var inputFolder = Path.Combine(toolingFolder, "SourceImages");
var outputFolder = Path.Combine(toolingFolder, "TargetImages");
Directory.CreateDirectory(outputFolder);

// Export an already converted PNG without resizing it again.
if (args.Length > 0 && args[0] == "--export")
{
    if (args.Length is < 2 or > 3)
        throw new ArgumentException("Usage: ImageConverter --export input.png [output.asm]");

    var png = Path.GetFullPath(args[1]);
    var asm = args.Length == 3 ? args[2] : Path.Combine(toolingFolder, "CbmSource", Path.GetFileNameWithoutExtension(png) + ".asm");
    CbmPrgStudioExporter.Save(png, asm);
    return;
}

var sourceFiles = args.Length == 0
    ? Directory.EnumerateFiles(inputFolder).Where(f => Path.GetExtension(f).Equals(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToArray()
    : args.Select(f => Path.GetFullPath(File.Exists(f) ? f : Path.Combine(inputFolder, f))).ToArray();

foreach (var source in sourceFiles)
{
    var target = Path.Combine(outputFolder, Path.GetFileName(source));
    Rasterize(source, target);
    Console.WriteLine($"{Path.GetFileName(source)} -> {target}");
    CbmPrgStudioExporter.Save(target, Path.Combine(toolingFolder, "CbmSource", Path.GetFileNameWithoutExtension(target) + ".asm"));
}

static void Rasterize(string sourceFile, string targetFile)
{
    const int width = 96, height = 42, scale = 8;
    using var source = new Bitmap(sourceFile);

    if (source.Width != width * scale || source.Height != height * scale)
        throw new ArgumentException($"Expected a 768x336 image, got {source.Width}x{source.Height}: {sourceFile}");

    // Average every source pixel in each 8x8 footprint to avoid aliasing.
    // Perceptual luminance deliberately discards hue: the output is a tonal palette.
    var tones = new double[width * height];
    
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            double sum = 0;

            for (var sy = 0; sy < scale; sy++)
            {
                for (var sx = 0; sx < scale; sx++)
                {
                    var c = source.GetPixel(x * scale + sx, y * scale + sy);
                    var alpha = c.A / 255.0;
                    sum += alpha * (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) + (1 - alpha) * 255;
                }
            }

            tones[y * width + x] = sum / (scale * scale);
        }
    }

    // A small unsharp mask restores edges softened by the reduction.
    var sharpened = new double[tones.Length];
    int[] kernel = [1, 2, 1];
    
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            double blurred = 0;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    blurred += kernel[dx + 1] * kernel[dy + 1] * tones[Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)];
                }
            }

            var i = y * width + x;
            sharpened[i] = Math.Clamp(tones[i] + 0.6 * (tones[i] - blurred / 16), 0, 255);
        }
    }

    // Fit three tonal clusters rather than using fixed RGB-distance thresholds.
    // This lets a dark or low-contrast photograph use the full output palette.
    var sorted = sharpened.OrderBy(v => v).ToArray();
    double[] centers = [sorted[sorted.Length / 6], sorted[sorted.Length / 2], sorted[sorted.Length * 5 / 6]];

    for (var iteration = 0; iteration < 100; iteration++)
    {
        var sums = new double[3];
        var counts = new int[3];

        foreach (var value in sharpened)
        {
            var index = Nearest(value, centers);
            sums[index] += value;
            counts[index]++;
        }

        double movement = 0;

        for (var i = 0; i < 3; i++)
        {
            var next = counts[i] == 0 ? centers[i] : sums[i] / counts[i];
            movement = Math.Max(movement, Math.Abs(next - centers[i]));
            centers[i] = next;
        }

        if (movement < 0.001)
            break;
    }

    Color[] palette = [Color.FromArgb(0x44, 0x44, 0x44), Color.FromArgb(0x58, 0x8D, 0x43), Color.FromArgb(0x9A, 0xD2, 0x84)];
    using var target = new Bitmap(width, height, PixelFormat.Format24bppRgb);
    
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            target.SetPixel(x, y, palette[Nearest(sharpened[y * width + x], centers)]);
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetFile))!);
    target.Save(targetFile, ImageFormat.Png);
}

static int Nearest(double value, double[] centers)
{
    var best = 0;

    for (var i = 1; i < centers.Length; i++)
    {
        if (Math.Abs(value - centers[i]) < Math.Abs(value - centers[best]))
            best = i;
    }

    return best;
}
