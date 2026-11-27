using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OrmPacker;

var dir = Path.Combine(Path.GetTempPath(), "ormpacker-tests");
Directory.CreateDirectory(dir);
var testSessionPath = Path.Combine(dir, "ui-session.json");
var failures = 0;

// Tests must not pick up the user's live session from %LOCALAPPDATA%.
MainForm NewForm(string[]? args = null)
{
    if (File.Exists(testSessionPath)) File.Delete(testSessionPath);
    return new MainForm(args, testSessionPath);
}

// Source-contract tests read the actual web sources: there is no browser test harness in v1.
string? RepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null && !File.Exists(Path.Combine(dir, "OrmPacker", "Core", "Localization.cs")))
        dir = Directory.GetParent(dir)?.FullName;
    return dir;
}

void Check(string name, bool condition, string details = "")
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}  {details}");
    if (!condition) failures++;
}

// Opt-in release helper: ORM_PACKER_SCREENSHOTS=1 writes docs/screenshots/{pack,convert}.png.
void GenerateScreenshots()
{
    var root = RepoRoot() ?? throw new DirectoryNotFoundException("Repository root not found.");
    var outDir = Path.Combine(root, "docs", "screenshots");
    Directory.CreateDirectory(outDir);
    Exception? error = null;
    var thread = new Thread(() =>
    {
        try
        {
            void Capture(MainForm form, string name)
            {
                form.ClientSize = new Size(1240, 860);
                form.Show();
                Application.DoEvents();
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height));
                image.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }

            var textures = Path.Combine(root, "textures");
            using (var pack = NewForm([
                Path.Combine(textures, "rusty_metal_04_ao_1k.jpg"),
                Path.Combine(textures, "rusty_metal_04_rough_1k.exr"),
                Path.Combine(textures, "rusty_metal_04_metal_1k.exr"),
                Path.Combine(textures, "rusty_metal_04_disp_1k.png")]))
                Capture(pack, "pack");

            var hdrpPath = Path.Combine(dir, "doc_hdrp_mask.png");
            Packer.SavePng(Packer.Pack(512, 512,
                new ChannelInput(null, SourceComponent.Red, 180, false),
                new ChannelInput(null, SourceComponent.Red, 60, false),
                new ChannelInput(null, SourceComponent.Red, 220, false),
                new ChannelInput(null, SourceComponent.Red, 90, false), true), hdrpPath, true);
            using var convert = NewForm();
            ((ModernComboBox)typeof(MainForm)
                .GetField("_modeBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(convert)!)
                .SelectedIndex = 1;
            typeof(MainForm).GetMethod("AddFiles", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(convert, new object?[] { new[] { hdrpPath }, null });
            Application.DoEvents();
            Capture(convert, "convert");
        }
        catch (Exception ex) { error = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (error is not null) throw error;
    Console.WriteLine($"Screenshots written to {outDir}");
}

if (Environment.GetEnvironmentVariable("ORM_PACKER_SCREENSHOTS") == "1")
{
    GenerateScreenshots();
    return;
}

IEnumerable<Control> Descendants(Control control)
{
    yield return control;
    foreach (Control child in control.Controls)
        foreach (var item in Descendants(child))
            yield return item;
}

static Color CenterPixel(PictureBox box)
{
    using var bitmap = new Bitmap(box.Width, box.Height);
    box.DrawToBitmap(bitmap, new Rectangle(0, 0, box.Width, box.Height));
    return bitmap.GetPixel(box.Width / 2, box.Height / 2);
}

static string SolidPng(string path, int width, int height, byte r, byte g, byte b)
{
    using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    var data = bitmap.LockBits(new Rectangle(0, 0, width, height),
        ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
    {
        var off = y * data.Stride + x * 4;
        Marshal.WriteByte(data.Scan0, off, b);
        Marshal.WriteByte(data.Scan0, off + 1, g);
        Marshal.WriteByte(data.Scan0, off + 2, r);
        Marshal.WriteByte(data.Scan0, off + 3, 255);
    }
    bitmap.UnlockBits(data);
    bitmap.Save(path, ImageFormat.Png);
    return path;
}

static string UncompressedTga(string path, byte[] bgr)
{
    var header = new byte[18];
    header[2] = 2;              // truecolor
    header[12] = 2; header[13] = 0; // width
    header[14] = 2; header[15] = 0; // height
    header[16] = 24;            // 24 bpp
    header[17] = 0;             // bottom-left origin
    File.WriteAllBytes(path, header.Concat(bgr).ToArray());
    return path;
}

// --- 1. Map detection by file name ---
Check("detect AO", Preset.DetectKind("T_Rock_AO.png") == MapKind.Occlusion);
Check("detect Roughness", Preset.DetectKind("T_Rock_Roughness.png") == MapKind.Roughness);
Check("detect Smoothness", Preset.DetectKind("T_Rock_Gloss.png") == MapKind.Smoothness);
Check("detect Metallic", Preset.DetectKind("T_Rock_Metallic.png") == MapKind.Metallic);
Check("detect Height", Preset.DetectKind("T_Rock_Height.png") == MapKind.Height);
Check("detect Detail", Preset.DetectKind("T_Rock_DetailMask.png") == MapKind.DetailMask);
Check("detect Normal (not Metallic)",
    Preset.DetectKind("rusty_metal_04_nor_gl_1k.exr") == MapKind.Normal);
Check("detect Metallic in material names",
    Preset.DetectKind("rusty_metal_04_metal_1k.exr") == MapKind.Metallic);
Check("detect AO in material names",
    Preset.DetectKind("rusty_metal_04_ao_1k.jpg") == MapKind.Occlusion);

// --- 2. ORM: R=AO, G=Roughness, B=Metallic ---
var ao = TextureImage.Load(SolidPng(Path.Combine(dir, "T_Rock_AO.png"), 2, 2, 10, 10, 10));
var rough = TextureImage.Load(SolidPng(Path.Combine(dir, "T_Rock_Roughness.png"), 2, 2, 20, 20, 20));
var metal = TextureImage.Load(SolidPng(Path.Combine(dir, "T_Rock_Metallic.png"), 2, 2, 30, 30, 30));

var red = new ChannelInput(ao, SourceComponent.Red, 255, false);
var green = new ChannelInput(rough, SourceComponent.Red, 255, false);
var blue = new ChannelInput(metal, SourceComponent.Red, 255, false);
var solid = new ChannelInput(null, SourceComponent.Red, 255, false);

var orm = Packer.Pack(2, 2, red, green, blue, solid, useAlpha: false);
Check("ORM R=AO", orm.Pixels[2] == 10, $"R={orm.Pixels[2]}");
Check("ORM G=Roughness", orm.Pixels[1] == 20, $"G={orm.Pixels[1]}");
Check("ORM B=Metallic", orm.Pixels[0] == 30, $"B={orm.Pixels[0]}");
Check("ORM A=255", orm.Pixels[3] == 255);

// --- 3. HDRP Mask Map: A=Smoothness with inversion ---
var smooth = TextureImage.Load(SolidPng(Path.Combine(dir, "T_Rock_Smoothness.png"), 2, 2, 40, 40, 40));
var smoothInverted = new ChannelInput(smooth, SourceComponent.Red, 255, true);
var hdrp = Packer.Pack(2, 2, blue, red, solid, smoothInverted, useAlpha: true);
Check("HDRP R=Metallic", hdrp.Pixels[2] == 30, $"R={hdrp.Pixels[2]}");
Check("HDRP G=AO", hdrp.Pixels[1] == 10, $"G={hdrp.Pixels[1]}");
Check("HDRP A=inverted smoothness", hdrp.Pixels[3] == 215, $"A={hdrp.Pixels[3]}");

// --- 4. TGA 24bpp, bottom-left origin (Y flip) ---
var tga = UncompressedTga(Path.Combine(dir, "test.tga"),
    [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);
var tgaImage = TextureImage.Load(tga);
var tgaChannel = new ChannelInput(tgaImage, SourceComponent.Red, 255, false);
var tgaPacked = Packer.Pack(2, 2, tgaChannel, solid, solid, solid, false);
Check("TGA bottom-origin flip",
    tgaPacked.Pixels[2] == 9 && tgaPacked.Pixels[6] == 12 && tgaPacked.Pixels[10] == 3,
    $"top R={tgaPacked.Pixels[2]}/{tgaPacked.Pixels[6]}, bottom R={tgaPacked.Pixels[10]}/{tgaPacked.Pixels[14]}");

// --- 5. Different input sizes: resampling works ---
var small = new ChannelInput(TextureImage.Load(
    SolidPng(Path.Combine(dir, "T_Rock_AO_1px.png"), 1, 1, 77, 77, 77)), SourceComponent.Red, 255, false);
var resized = Packer.Pack(2, 2, small, green, blue, solid, false);
Check("resample to 2x2", resized.Pixels[2] == 77 && resized.Pixels[10] == 77,
    $"R={resized.Pixels[2]}/{resized.Pixels[10]}");

// --- 6. PNG lossless ---
var outPath = Path.Combine(dir, "T_Rock_ORM.png");
Packer.SavePng(orm, outPath, alpha: false);
var reloaded = TextureImage.Load(outPath);
var again = Packer.Pack(2, 2,
    new ChannelInput(reloaded, SourceComponent.Red, 255, false),
    new ChannelInput(reloaded, SourceComponent.Green, 255, false),
    new ChannelInput(reloaded, SourceComponent.Blue, 255, false),
    solid, false);
Check("PNG round-trip", again.Pixels[0] == 30 && again.Pixels[1] == 20 && again.Pixels[2] == 10,
    $"B={again.Pixels[0]}, G={again.Pixels[1]}, R={again.Pixels[2]}");

// --- 6b. All output formats ---
foreach (var format in OutputFormat.All)
{
    var outputPath = Path.Combine(dir, "format_test" + format.Extension);
    Packer.Save(orm, outputPath, format.Format, false);
    Check($"format {format.Name}", File.Exists(outputPath) && new FileInfo(outputPath).Length > 20,
        $"{new FileInfo(outputPath).Length} bytes");
    var reloadedFormat = TextureImage.Load(outputPath);
    var againFormat = Packer.Pack(2, 2,
        new ChannelInput(reloadedFormat, SourceComponent.Red, 255, false),
        new ChannelInput(reloadedFormat, SourceComponent.Green, 255, false),
        new ChannelInput(reloadedFormat, SourceComponent.Blue, 255, false),
        solid, false);
    var tolerance = format.Format == SaveFormat.Jpeg ? 12 : 0;
    var ok = Math.Abs(againFormat.Pixels[0] - 30) <= tolerance &&
             Math.Abs(againFormat.Pixels[1] - 20) <= tolerance &&
             Math.Abs(againFormat.Pixels[2] - 10) <= tolerance;
    Check($"reads back: {format.Name}", ok,
        $"B={againFormat.Pixels[0]}, G={againFormat.Pixels[1]}, R={againFormat.Pixels[2]}");
}
Check("name with format extension",
    OutputNaming.Suggest(["T_Rock_AO.png"], "_ORM", ".jpg") == "T_Rock_ORM.jpg");

// --- 7. Performance at 4096x4096 ---
var big = TextureImage.Load(SolidPng(Path.Combine(dir, "big.png"), 4096, 4096, 128, 128, 128));
var sw = System.Diagnostics.Stopwatch.StartNew();
var bigPacked = Packer.Pack(4096, 4096,
    new ChannelInput(big, SourceComponent.Red, 255, false),
    new ChannelInput(big, SourceComponent.Green, 255, false),
    new ChannelInput(big, SourceComponent.Blue, 255, false), solid, false);
sw.Stop();
Check("4096x4096 pack", bigPacked.Pixels.Length == 4096 * 4096 * 4, $"{sw.ElapsedMilliseconds} ms");

// --- 8. Real EXR files from the textures folder (DWAA) ---
var textureDir = AppContext.BaseDirectory;
while (textureDir is not null && !Directory.Exists(Path.Combine(textureDir, "textures")))
    textureDir = Directory.GetParent(textureDir)?.FullName;

if (textureDir is not null)
{
    var exrPath = Path.Combine(textureDir, "textures", "rusty_metal_04_metal_1k.exr");
    var exrWatch = Stopwatch.StartNew();
    var metalExr = TextureImage.Load(exrPath);
    exrWatch.Stop();
    Check("EXR 1024x1024", metalExr.Width == 1024 && metalExr.Height == 1024,
        $"{metalExr.Width}x{metalExr.Height}, {exrWatch.ElapsedMilliseconds} ms");

    var exrPacked = Packer.Pack(16, 16,
        new ChannelInput(metalExr, SourceComponent.Red, 255, false),
        solid, solid, solid, false);
    byte min = 255, max = 0;
    for (var i = 2; i < exrPacked.Pixels.Length; i += 4)
    {
        min = Math.Min(min, exrPacked.Pixels[i]);
        max = Math.Max(max, exrPacked.Pixels[i]);
    }
    Check("EXR values are not empty", max > 0, $"min={min}, max={max}");
    Check("EXR kind from file name", Preset.DetectKind(Path.GetFileName(exrPath)) == MapKind.Metallic);
}
else
{
    Console.WriteLine("SKIP  textures folder not found");
}

// --- 9. File naming ---
Check("name: common prefix",
    OutputNaming.Suggest(["T_Rock_AO.png", "T_Rock_Roughness.png", "T_Rock_Metallic.png"], "_ORM")
    == "T_Rock_ORM.png");
Check("name: technical words removed",
    OutputNaming.Suggest([
        "rusty_metal_04_ao_1k.jpg", "rusty_metal_04_rough_1k.exr", "rusty_metal_04_metal_1k.exr"
    ], "_ORM") == "rusty_metal_04_ORM.png",
    OutputNaming.Suggest([
        "rusty_metal_04_ao_1k.jpg", "rusty_metal_04_rough_1k.exr", "rusty_metal_04_metal_1k.exr"
    ], "_ORM"));
Check("name: different prefixes",
    OutputNaming.Suggest(["Brick_Albedo.png", "Stone_AO.png"], "_ORM") == "Brick_ORM.png",
    OutputNaming.Suggest(["Brick_Albedo.png", "Stone_AO.png"], "_ORM"));
Check("name: no files",
    OutputNaming.Suggest([], "_MaskMap") == "Texture_MaskMap.png");

// --- 10. Output sizes ---
Check("output size original", OutputSize.Original.Apply(2048, 1024) == (2048, 1024));
Check("output size 1024 keeps aspect", OutputSize.Default.Apply(2048, 1024) == (1024, 512),
    $"{OutputSize.Default.Apply(2048, 1024)}");
Check("output size 512", OutputSize.All.First(s => s.Pixels == 512).Apply(1024, 1024) == (512, 512));
Check("output size items", OutputSize.All.Count == 9);
Check("output size label", OutputSize.Default.DisplayName.Contains("1024"));

// --- 11. Custom preset and channel previews ---
Check("custom preset", Preset.Custom.Channels.Count == 4 &&
    Preset.Custom.Channels.Values.All(c => c.HasMap));
var channelTest = Packer.Pack(1, 1, red, green, blue, solid, true);
foreach ((var channel, var expected) in new[]
         {
             (Channel.R, (byte)10), (Channel.G, (byte)20),
             (Channel.B, (byte)30), (Channel.A, (byte)255)
         })
{
    using var bitmap = Packer.ToChannelBitmap(channelTest, channel);
    var sample = bitmap.GetPixel(0, 0);
    Check($"channel preview {channel}", sample.R == expected && sample.G == expected && sample.B == expected,
        $"value={sample.R}");
}

// --- 12. Localization ---
Localization.Initialize(new System.Globalization.CultureInfo("ru-RU"));
Check("locale ru", Localization.Language == "ru" &&
    Localization.Get("save.button") == "Сохранить PNG");
Localization.Initialize(new System.Globalization.CultureInfo("en-US"));
Check("locale en", Localization.Language == "en" &&
    Localization.Get("save.button") == "Save PNG");
Check("preset localized name", Preset.Orm.DisplayName.Contains("ORM"));
Localization.SetLanguage("ru");
var before = Localization.Get("save.button");
Localization.Toggle();
Check("language toggle",
    before == "Сохранить PNG" && Localization.Get("save.button") == "Save PNG");
Localization.SetLanguage("en");

// --- 13. Form initialization (without showing) ---
Exception? uiError = null;
var uiThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        Check("UI initialization", form.Controls.Count == 1 &&
            form.Controls[0].Padding.Left == 16 && form.Controls[0].Padding.Right == 16);
        Check("UI: form uses the bundled application icon", form.Icon is not null &&
            typeof(MainForm).Assembly.GetManifestResourceNames().Contains("OrmPacker.orm-packer.ico"));
        var field = typeof(MainForm).GetField("_saveButton", BindingFlags.NonPublic | BindingFlags.Instance);
        var button = (Control)field!.GetValue(form)!;
        Check("save button in form", button.Bounds.X == 12 && button.Parent is not null,
            $"X={button.Bounds.X}, parent={button.Parent?.GetType().Name}");
        // The rounded button must not have a straight border along its edges.
        // Enable it first: the paint probe checks the normal (not dimmed) state.
        var save = (ModernButton)button;
        save.Enabled = true;
        using var probe = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(probe, new Rectangle(0, 0, button.Width, button.Height));
        var top = probe.GetPixel(button.Width / 2, 0);
        var left = probe.GetPixel(0, button.Height / 2);
        var corner = probe.GetPixel(0, 0);
        Check("save button: no straight border around the corners",
            top == save.BaseColor && left == save.BaseColor && corner != save.BaseColor,
            $"top={top}, left={left}, corner={corner}");

        var slotsField = typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance);
        var firstSlot = (Control)((Array)slotsField!.GetValue(form)!).GetValue(0)!;
        var slider = Descendants(firstSlot).OfType<ModernTrackBar>().FirstOrDefault();
        Check("value slider 0..255",
            slider is not null && slider.Minimum == 0 && slider.Maximum == 255 && slider.Value == 255);

        var numberBox = Descendants(firstSlot).OfType<ModernNumberBox>().FirstOrDefault();
        Check("dark number box", numberBox is not null && numberBox.Value == 255);

        var firstThumb = Descendants(firstSlot).OfType<PictureBox>().FirstOrDefault();
        Check("empty slot thumbnail shows its constant value", firstThumb?.Image is not null &&
            CenterPixel(firstThumb) == Color.FromArgb(255, 255, 255, 255),
            firstThumb?.Image is null ? "no image" : CenterPixel(firstThumb!).ToString());
        if (numberBox is not null)
        {
            numberBox.Value = 47;
            Check("thumbnail follows the constant value", CenterPixel(firstThumb!) == Color.FromArgb(255, 47, 47, 47),
                CenterPixel(firstThumb!).ToString());
        }

        var invertField = firstSlot.GetType().GetField("InvertValue",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
        invertField.SetValue(firstSlot, true);
        var clearButton = Descendants(firstSlot).OfType<ModernButton>().First(b => b.Text == "×");
        typeof(ModernButton).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(clearButton, new object[] { EventArgs.Empty });
        Check("clearing a slot resets inversion too", (bool)invertField.GetValue(firstSlot)! is false);
        Check("slot stats and warnings are visible",
            Descendants(firstSlot).OfType<Label>().Count(l => l.Text.Length > 0) >= 2 &&
            Descendants(firstSlot).OfType<Label>().Any(l =>
                l.Text.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
                l.Text.Contains("пуст", StringComparison.OrdinalIgnoreCase)));

        string[] tabs = ["RGB", "RGBA", "R", "G", "B", "A"];
        var viewTabs = Descendants(form).OfType<Button>()
            .Count(b => tabs.Contains(b.Text));
        Check("preview tabs RGB/RGBA/R/G/B/A", viewTabs == 6, $"tabs={viewTabs}");
        Check("language switch buttons", Descendants(form).OfType<ModernButton>()
            .Any(b => b.Text is "RU" or "EN"));
        var subtitleControl = (Control)typeof(MainForm)
            .GetField("_subtitle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var presetComboControl = (Control)typeof(MainForm)
            .GetField("_presetBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        Check("subtitle stays inside the header", subtitleControl.Right <= form.ClientSize.Width &&
            subtitleControl.Top < 60 && !presetComboControl.Visible,
            $"subtitle right={subtitleControl.Right}, form={form.ClientSize.Width}");

        var setMode = typeof(MainForm).GetMethod("SetPreviewMode", BindingFlags.NonPublic | BindingFlags.Instance);
        var buttonsField = typeof(MainForm).GetField("_viewButtons", BindingFlags.NonPublic | BindingFlags.Instance);
        setMode!.Invoke(form, new object?[] { Enum.Parse(typeof(MainForm).GetNestedType("PreviewMode", BindingFlags.NonPublic)!, "G") });
        var buttons = (System.Collections.IDictionary)buttonsField!.GetValue(form)!;
        var selected = buttons.Values.Cast<Button>().Count(b => b.Tag is bool s && s);
        Check("exactly one active tab", selected == 1, $"selected={selected}");

        var presetBoxField = typeof(MainForm).GetField("_presetBox", BindingFlags.NonPublic | BindingFlags.Instance);
        var combo = (ModernComboBox)presetBoxField!.GetValue(form)!;
        typeof(ModernComboBox).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(combo, new object[] { EventArgs.Empty });
        var popup = (ToolStripDropDown)typeof(ModernComboBox)
            .GetField("_popup", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(combo)!;
        Check("popup width equals list width", popup.Width == combo.Width + 2,
            $"popup={popup.Width}, combo={combo.Width}");

    }
    catch (Exception ex) { uiError = ex; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
Check("UI without exceptions", uiError is null, uiError?.ToString() ?? "");

// --- 14. Largest-area sizing and channel-type filter ---
var wide = TextureImage.Load(SolidPng(Path.Combine(dir, "wide.png"), 2048, 512, 90, 90, 90));
var square = TextureImage.Load(SolidPng(Path.Combine(dir, "square.png"), 500, 500, 90, 90, 90));
var wideInput = new ChannelInput(wide, SourceComponent.Red, 255, false);
var squareInput = new ChannelInput(square, SourceComponent.Red, 255, false);
Check("Original = largest-area map",
    OutputSizing.SourceSize(squareInput, wideInput) == (2048, 512),
    $"{OutputSizing.SourceSize(squareInput, wideInput)}");
Check("Original keeps largest-area aspect ratio",
    OutputSizing.TargetSize(OutputSize.Default, squareInput, wideInput) == (1024, 256),
    $"{OutputSizing.TargetSize(OutputSize.Default, squareInput, wideInput)}");

static string RowPng(string path, byte[] values)
{
    using var bitmap = new Bitmap(values.Length, 1, PixelFormat.Format32bppArgb);
    var data = bitmap.LockBits(new Rectangle(0, 0, values.Length, 1),
        ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    for (var x = 0; x < values.Length; x++)
    {
        var off = x * 4;
        Marshal.WriteByte(data.Scan0, off, values[x]);
        Marshal.WriteByte(data.Scan0, off + 1, values[x]);
        Marshal.WriteByte(data.Scan0, off + 2, values[x]);
        Marshal.WriteByte(data.Scan0, off + 3, 255);
    }
    bitmap.UnlockBits(data);
    bitmap.Save(path, ImageFormat.Png);
    return path;
}
var row = TextureImage.Load(RowPng(Path.Combine(dir, "row.png"), [10, 20, 30, 40]));
var nearest = Packer.Pack(2, 1,
    new ChannelInput(row, SourceComponent.Red, 255, false, ResampleFilter.Nearest),
    solid, solid, solid, false);
var bilinear = Packer.Pack(2, 1,
    new ChannelInput(row, SourceComponent.Red, 255, false, ResampleFilter.Bilinear),
    solid, solid, solid, false);
Check("nearest on downscale", nearest.Pixels[2] == 20 && nearest.Pixels[6] == 40,
    $"R={nearest.Pixels[2]}/{nearest.Pixels[6]}");
Check("bilinear on downscale", bilinear.Pixels[2] == 15 && bilinear.Pixels[6] == 35,
    $"R={bilinear.Pixels[2]}/{bilinear.Pixels[6]}");
Check("filter by channel kind",
    Preset.FilterFor(MapKind.Metallic) == ResampleFilter.Nearest &&
    Preset.FilterFor(MapKind.DetailMask) == ResampleFilter.Nearest &&
    Preset.FilterFor(MapKind.Occlusion) == ResampleFilter.Bilinear &&
    Preset.FilterFor(MapKind.Roughness) == ResampleFilter.Bilinear &&
    Preset.FilterFor(MapKind.Smoothness) == ResampleFilter.Bilinear &&
    Preset.FilterFor(MapKind.Height) == ResampleFilter.Bilinear);

// --- 15. Raw values: constant channel and no gamma conversion ---
var constantPack = Packer.Pack(3, 3,
    new ChannelInput(null, SourceComponent.Red, 77, false),
    new ChannelInput(ao, SourceComponent.Red, 255, false),
    blue, solid, false);
Check("constant passes through packing unchanged",
    Enumerable.Range(0, 9).All(i => constantPack.Pixels[i * 4 + 2] == 77));

var constantImage = TextureImage.Load(SolidPng(Path.Combine(dir, "constant.png"), 4, 4, 201, 201, 201));
var constantImagePack = Packer.Pack(2, 2,
    new ChannelInput(constantImage, SourceComponent.Red, 255, false),
    solid, solid, solid, false);
Check("constant map passes through packing unchanged",
    Enumerable.Range(0, 4).All(i => constantImagePack.Pixels[i * 4 + 2] == 201));

static uint PngCrc(byte[] data)
{
    var crc = 0xffffffff;
    foreach (var value in data)
    {
        crc ^= value;
        for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
    }
    return ~crc;
}
static string GammaOnlyPng(string path, int gamma, byte value)
{
    var source = SolidPng(path + ".src.png", 2, 2, value, value, value);
    var bytes = File.ReadAllBytes(source);
    var result = new List<byte>(bytes.Take(33)); // signature + IHDR
    var chunk = new byte[16];
    chunk[3] = 4;
    Encoding.ASCII.GetBytes("gAMA").CopyTo(chunk, 4);
    chunk[8] = (byte)(gamma >> 24); chunk[9] = (byte)(gamma >> 16);
    chunk[10] = (byte)(gamma >> 8); chunk[11] = (byte)gamma;
    var crc = PngCrc(chunk.Skip(4).Take(8).ToArray());
    chunk[12] = (byte)(crc >> 24); chunk[13] = (byte)(crc >> 16);
    chunk[14] = (byte)(crc >> 8); chunk[15] = (byte)crc;
    result.AddRange(chunk);
    // Keep only IDAT and IEND: sRGB/gAMA/pHYs chunks from GDI+ are removed.
    var pos = 33;
    while (pos + 12 <= bytes.Length)
    {
        var len = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
        var type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
        if (type is "IDAT" or "IEND") result.AddRange(bytes.Skip(pos).Take(12 + len));
        pos += 12 + len;
        if (type == "IEND") break;
    }
    File.WriteAllBytes(path, result.ToArray());
    File.Delete(source);
    return path;
}
var gammaImage = TextureImage.Load(GammaOnlyPng(Path.Combine(dir, "gamma.png"), 220000, 128));
var gammaPack = Packer.Pack(1, 1,
    new ChannelInput(gammaImage, SourceComponent.Red, 255, false), solid, solid, solid, false);
Check("gAMA in PNG does not change the data", gammaPack.Pixels[2] == 128,
    $"R={gammaPack.Pixels[2]}");

// --- 16. Deterministic output ---
foreach (var format in OutputFormat.All)
{
    var a = Path.Combine(dir, "determinism_a" + format.Extension);
    var b = Path.Combine(dir, "determinism_b" + format.Extension);
    Packer.Save(orm, a, format.Format, false);
    Packer.Save(orm, b, format.Format, false);
    Check($"determinism {format.Name}",
        SHA256.HashData(File.ReadAllBytes(a)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(b))));
}
static List<string> PngChunks(string path)
{
    var bytes = File.ReadAllBytes(path);
    var types = new List<string>();
    var pos = 8;
    while (pos + 12 <= bytes.Length)
    {
        var len = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
        types.Add(Encoding.ASCII.GetString(bytes, pos + 4, 4));
        pos += 12 + len;
        if (types[^1] == "IEND") break;
    }
    return types;
}
var deterministicPng = Path.Combine(dir, "determinism_a.png");
Check("PNG without color/timestamp chunks",
    PngChunks(deterministicPng).All(t => t is "IHDR" or "IDAT" or "IEND"),
    string.Join(",", PngChunks(deterministicPng)));

// --- 17. Rounding of 16-bit input ---
static string Tga16(string path, byte imageType, params byte[][] pixels)
{
    var header = new byte[18];
    header[2] = imageType;
    header[12] = (byte)pixels.Length; header[13] = 0;
    header[14] = 1; header[15] = 0;
    header[16] = 16; header[17] = 0;
    var bytes = new List<byte>(header);
    foreach (var pixel in pixels) bytes.AddRange(pixel);
    File.WriteAllBytes(path, bytes.ToArray());
    return path;
}
byte[][] fiveBit = [[0, 0], [3, 0], [16, 0], [17, 0], [31, 0]];
var tga16Image = TextureImage.Load(Tga16(Path.Combine(dir, "tga16.tga"), 2, fiveBit));
var tga16Pack = Packer.Pack(5, 1,
    new ChannelInput(tga16Image, SourceComponent.Blue, 255, false),
    solid, solid, solid, false);
Check("TGA 16bpp: 5-bit value is rounded",
    Enumerable.Range(0, 5).Select(i => tga16Pack.Pixels[i * 4 + 2])
        .SequenceEqual(new byte[] { 0, 25, 132, 140, 255 }),
    string.Join(",", Enumerable.Range(0, 5).Select(i => tga16Pack.Pixels[i * 4 + 2])));

var tga16Gray = TextureImage.Load(Tga16(Path.Combine(dir, "tga16gray.tga"), 3, [77, 42]));
var grayPack = Packer.Pack(1, 1,
    new ChannelInput(tga16Gray, SourceComponent.Red, 255, false),
    solid, solid,
    new ChannelInput(tga16Gray, SourceComponent.Alpha, 255, false), true);
Check("TGA 16bpp gray: low byte is luminance, high byte is alpha",
    grayPack.Pixels[2] == 77 && grayPack.Pixels[3] == 42,
    $"R={grayPack.Pixels[2]}, A={grayPack.Pixels[3]}");

var exrToByte = typeof(Packer).Assembly.GetType("OrmPacker.ExrDecoder")!
    .GetMethod("ToByte", BindingFlags.NonPublic | BindingFlags.Static)!;
var exrActual = new ushort[] { 0, 255, 256, 511, 32768, 65535 }
    .Select(v => (byte)exrToByte.Invoke(null, [v])!).ToArray();
Check("EXR 16→8: clamp + rounding",
    exrActual.SequenceEqual(new byte[] { 0, 1, 1, 2, 128, 255 }),
    string.Join(",", exrActual));

// --- 18. Slot diagnostics ---
var solidStats = SlotDiagnostics.Stats(new ChannelInput(null, SourceComponent.Red, 64, true));
Check("constant stats with inversion",
    solidStats.Min == 191 && solidStats.Max == 191 && solidStats.Avg == 191 && solidStats.IsConstant,
    $"{solidStats}");
var rowStats = SlotDiagnostics.Stats(new ChannelInput(row, SourceComponent.Red, 255, false));
Check("stats min/max/avg", rowStats.Min == 10 && rowStats.Max == 40 && rowStats.Avg == 25 && !rowStats.IsConstant,
    $"{rowStats}");

ChannelInput diagSolid = new(null, SourceComponent.Red, 255, false);
ChannelInput diagSmall = new(ao, SourceComponent.Red, 255, false);
ChannelInput diagBig = new(TextureImage.Load(SolidPng(Path.Combine(dir, "diag_big.png"), 8, 8, 30, 30, 30)),
    SourceComponent.Red, 255, false);
Check("warning: empty channel",
    SlotDiagnostics.Warnings([diagSmall, diagSolid, diagSolid, diagSolid], 1)
        .Contains(SlotWarning.Empty));
Check("warning: constant",
    SlotDiagnostics.Warnings([diagSmall, diagBig, diagSolid, diagSolid], 1)
        .Contains(SlotWarning.Constant));
var emptyWarnings = SlotDiagnostics.Warnings([diagSmall, diagSolid, diagSolid, diagSolid], 1);
Check("empty slot never reports constant",
    emptyWarnings.Count == 1 && emptyWarnings[0] == SlotWarning.Empty,
    string.Join(",", emptyWarnings));
Check("fresh session keeps diagnostics silent",
    SlotDiagnostics.Warnings([diagSolid, diagSolid, diagSolid, diagSolid], 0, null, false).Count == 0);
Check("warning: size mismatch",
    SlotDiagnostics.Warnings([diagSmall, diagBig, diagSolid, diagSolid], 0)
        .Contains(SlotWarning.SizeMismatch));
Check("warning: duplicate source",
    SlotDiagnostics.Warnings([diagSmall, diagSmall, diagSolid, diagSolid], 0)
        .Contains(SlotWarning.DuplicateSource));

// --- 18a. Fresh desktop session emits no slot warnings ---
Exception? freshSessionError = null;
var freshSessionThread = new Thread(() =>
{
    try
    {
        string[] SlotNotices(MainForm form)
        {
            var slots = (Array)typeof(MainForm)
                .GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            var texts = new List<string>();
            foreach (var slot in slots)
            {
                var parts = new List<string>();
                foreach (var name in new[] { "Notice", "Notice2" })
                {
                    var property = slot!.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
                    parts.Add(((Label)property.GetValue(slot)!).Text);
                }
                texts.Add(string.Join("|", parts));
            }
            return texts.ToArray();
        }

        using var emptyForm = NewForm();
        var quiet = SlotNotices(emptyForm);
        Check("fresh desktop session shows no slot warnings",
            quiet.All(t => t.Replace("|", "").Length == 0), string.Join("|", quiet));

        var firstMap = SolidPng(Path.Combine(dir, "fresh_first_map.png"), 4, 4, 10, 20, 30);
        using var loadedForm = NewForm([firstMap]);
        var afterLoad = SlotNotices(loadedForm);
        Check("loaded session prints no constant warning for empty slots",
            afterLoad.Skip(1).All(t => !t.Contains("constant", StringComparison.OrdinalIgnoreCase) &&
                                       !t.Contains("констант", StringComparison.OrdinalIgnoreCase)),
            string.Join("|", afterLoad));
    }
    catch (Exception ex) { freshSessionError = ex; }
});
freshSessionThread.SetApartmentState(ApartmentState.STA);
freshSessionThread.Start();
freshSessionThread.Join();
Check("fresh session UI without exceptions", freshSessionError is null, freshSessionError?.Message ?? "");

// --- 18b. Actions are disabled without inputs ---
Exception? disabledActionsError = null;
var disabledActionsThread = new Thread(() =>
{
    try
    {
        Control FieldOf(MainForm form, string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        System.Collections.IDictionary TabsOf(MainForm form) => (System.Collections.IDictionary)typeof(MainForm)
            .GetField("_viewButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        bool ActionsOn(MainForm form) => FieldOf(form, "_saveButton").Enabled &&
            FieldOf(form, "_unpackFooterButton").Enabled && FieldOf(form, "_clearButton").Enabled;
        bool ChannelTabsOn(MainForm form) => TabsOf(form).Keys.Cast<object>()
            .Where(key => new[] { "R", "G", "B", "A" }.Contains(key.ToString()))
            .All(key => ((Button)TabsOf(form)[key]!).Enabled);
        bool BaseTabsOn(MainForm form) => TabsOf(form).Keys.Cast<object>()
            .Where(key => key.ToString() is "Rgb" or "Rgba")
            .All(key => ((Button)TabsOf(form)[key]!).Enabled);

        using var empty = NewForm();
        var filesCount = ((System.Collections.ICollection)typeof(MainForm)
            .GetField("_files", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(empty)!).Count;
        var convertMode = (bool)typeof(MainForm)
            .GetField("_convertMode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(empty)!;
        Check("no inputs disables Save/Unpack/Clear", !ActionsOn(empty),
            $"files={filesCount}, convert={convertMode}, save={FieldOf(empty, "_saveButton").Enabled}, unpack={FieldOf(empty, "_unpackFooterButton").Enabled}, clear={FieldOf(empty, "_clearButton").Enabled}");
        Check("no inputs disables R/G/B/A tabs", !ChannelTabsOn(empty) && BaseTabsOn(empty),
            $"channels={ChannelTabsOn(empty)}, base={BaseTabsOn(empty)}");

        var input = SolidPng(Path.Combine(dir, "actions_input.png"), 4, 4, 12, 34, 56);
        using var loaded = NewForm([input]);
        Check("loaded map enables actions and channel tabs", ActionsOn(loaded) && ChannelTabsOn(loaded),
            $"actions={ActionsOn(loaded)}, channels={ChannelTabsOn(loaded)}");

        using var convertEmpty = NewForm();
        ((ModernComboBox)FieldOf(convertEmpty, "_modeBox")).SelectedIndex = 1;
        Check("convert without a source disables actions", !ActionsOn(convertEmpty));
        typeof(MainForm).GetField("_convertSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(convertEmpty, TextureImage.Load(input));
        typeof(MainForm).GetMethod("UpdateStatusForCurrentMap", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(convertEmpty, new object?[] { null });
        Check("convert source enables actions", ActionsOn(convertEmpty));
    }
    catch (Exception ex) { disabledActionsError = ex; }
});
disabledActionsThread.SetApartmentState(ApartmentState.STA);
disabledActionsThread.Start();
disabledActionsThread.Join();
Check("disabled actions UI without exceptions", disabledActionsError is null, disabledActionsError?.Message ?? "");

Check("RU/EN keys are in sync",
    Localization.EnglishStrings.Keys.OrderBy(k => k).SequenceEqual(
        Localization.RussianStrings.Keys.OrderBy(k => k)),
    $"EN={Localization.EnglishStrings.Count}, RU={Localization.RussianStrings.Count}");

// --- 18c. Locale punctuation parity ---
string SentenceEnding(string value)
{
    var trimmed = value.TrimEnd();
    return trimmed.Length > 0 && ":.!?".Contains(trimmed[^1]) ? trimmed[^1].ToString() : "";
}
var punctuationDrifts = Localization.EnglishStrings.Keys
    .Where(key => Localization.RussianStrings.TryGetValue(key, out var ru) &&
                  SentenceEnding(Localization.EnglishStrings[key]) != SentenceEnding(ru))
    .Select(key => $"{key}: '{Localization.EnglishStrings[key]}' / '{Localization.RussianStrings[key]}'")
    .ToArray();
Check("RU/EN dictionaries agree on trailing punctuation", punctuationDrifts.Length == 0,
    string.Join("; ", punctuationDrifts));

var punctuationRazor = RepoRoot() is { } root1
    ? Path.Combine(root1, "web", "OrmPacker.Web", "Pages", "Index.razor") : "";
var razorPairs = File.Exists(punctuationRazor) ? File.ReadAllText(punctuationRazor) : "";
var inlineDrifts = Regex.Matches(razorPairs, "T\\(\"([^\"]*)\", \"([^\"]*)\"\\)")
    .Where(match => SentenceEnding(match.Groups[1].Value) != SentenceEnding(match.Groups[2].Value))
    .Select(match => $"{match.Groups[1].Value} / {match.Groups[2].Value}")
    .ToArray();
Check("web inline RU/EN strings agree on trailing punctuation", inlineDrifts.Length == 0,
    string.Join("; ", inlineDrifts));

// --- 18d. Per-shell subtitle ---
Check("subtitle: desktop on your machine, web in your browser",
    Localization.EnglishStrings["app.subtitle"].Contains("on your machine") &&
    Localization.RussianStrings["app.subtitle"].Contains("на вашем компьютере") &&
    !Localization.EnglishStrings["app.subtitle"].Contains("browser") &&
    !Localization.RussianStrings["app.subtitle"].Contains("браузер") &&
    Localization.EnglishStrings["app.subtitle.web"].Contains("in your browser") &&
    Localization.RussianStrings["app.subtitle.web"].Contains("в браузере"));
var subtitleRazor = RepoRoot() is { } root0
    ? Path.Combine(root0, "web", "OrmPacker.Web", "Pages", "Index.razor") : "";
Check("web subtitle comes from the shared dictionary",
    File.Exists(subtitleRazor) &&
    File.ReadAllText(subtitleRazor).Contains("Localization.Get(\"app.subtitle.web\")") &&
    !File.ReadAllText(subtitleRazor).Contains("Everything is processed locally in your browser"));
Check("web slots use session-aware diagnostics",
    File.Exists(subtitleRazor) &&
    File.ReadAllText(subtitleRazor).Contains("SlotDiagnostics.Warnings(_inputs, i, stats, HasAnyMap)"));
var actionsRazor = File.Exists(subtitleRazor) ? File.ReadAllText(subtitleRazor) : "";
Check("web disables actions and channel tabs without inputs",
    actionsRazor.Contains("@onclick=\"Save\" disabled=\"@(!HasInputs)\"") &&
    actionsRazor.Contains("@onclick=\"ClearAll\" disabled=\"@(!HasInputs)\"") &&
    actionsRazor.Contains("disabled=\"@(channelTab && !HasInputs)\""));
Check("web unpacks the full-resolution current result",
    actionsRazor.Contains("var full = Packer.Pack(_targetWidth, _targetHeight,") &&
    actionsRazor.Contains("ormInterop.setSource\", _targetWidth, _targetHeight") &&
    Localization.EnglishStrings.ContainsKey("unpack.tip") &&
    Localization.RussianStrings.ContainsKey("unpack.tip"));
var langRoot = RepoRoot();
var interopText = langRoot is null ? "" :
    File.ReadAllText(Path.Combine(langRoot, "web", "OrmPacker.Web", "wwwroot", "js", "interop.js"));
Check("web persists language and updates html lang",
    actionsRazor.Contains("public string Language { get; set; }") &&
    actionsRazor.Contains("Language = Localization.Language,") &&
    actionsRazor.Contains("Localization.SetLanguage(session.Language") &&
    actionsRazor.Contains("ormInterop.setLanguage\", Localization.Language") &&
    interopText.Contains("document.documentElement.lang = language === 'ru' ? 'ru' : 'en';"));
Check("web header shows the app icon and a GitHub link",
    actionsRazor.Contains("src=\"favicon.png\" alt=\"\" width=\"32\" height=\"32\"") &&
    actionsRazor.Contains("class=\"github-link\"") &&
    actionsRazor.Contains("https://github.com/Kiyzo/orm-packer"));
var mainFormSource = langRoot is null ? "" :
    File.ReadAllText(Path.Combine(langRoot, "OrmPacker", "MainForm.cs"));
Check("desktop header keeps rounded corners behind the language toggle",
    mainFormSource.Contains("Padding = new Padding(16, 0, 16, 16)") &&
    mainFormSource.Contains("content.Controls.Add(header)") &&
    mainFormSource.Contains("Width = 108, BackColor = Color.Transparent"));
Localization.SetLanguage(Localization.Russian);
Check("Russian web subtitle is localized",
    Localization.Get("app.subtitle.web").Contains("в браузере"));
Localization.SetLanguage(Localization.English);
var desktopRoot = RepoRoot();
var desktopSource = desktopRoot is null ? "" : File.ReadAllText(Path.Combine(desktopRoot, "OrmPacker", "MainForm.cs"));
Check("step 3 Save highlights only after a real save",
    actionsRazor.Contains("_savedInSession ? \"done\"") &&
    desktopSource.Contains("_savedInSession = true;") &&
    desktopSource.Contains("_savedInSession") &&
    !actionsRazor.Contains("!string.IsNullOrEmpty(_fileName) ? \"done\""));

// --- 18e. Web bundle license attribution ---
var legalRoot = RepoRoot();
var noticeText = legalRoot is null ? "" : File.ReadAllText(Path.Combine(legalRoot, "NOTICE.md"));
var licenseText = legalRoot is null ? "" : File.ReadAllText(Path.Combine(legalRoot, "THIRD_PARTY_LICENSES.md"));
var webLock = legalRoot is null ? "" : File.ReadAllText(Path.Combine(legalRoot, "web", "package-lock.json"));
foreach (var package in new[] { "exrs", "exrs-raw-wasm-bindgen", "shallow-equals" })
{
    var inLock = webLock.Contains($"node_modules/{package}");
    Check($"npm bundle attributed: {package}",
        inLock && noticeText.Contains(package) && licenseText.Contains($"## {package}"),
        $"lock={inLock}, NOTICE={noticeText.Contains(package)}, licenses={licenseText.Contains($"## {package}")}");
}
Check("bundled license texts include MIT and BSD holders",
    licenseText.Contains("Copyright (c) 2014 Hugh Kennedy") &&
    licenseText.Contains("Copyright (c) Johannes Vollmer and contributors") &&
    noticeText.Contains("Build-time only"));
Check("wasm-bindgen runtime is attributed in both legal files",
    licenseText.Contains("wasm-bindgen runtime — MIT OR Apache-2.0") &&
    licenseText.Contains("Copyright (c) 2014 Alex Crichton") &&
    noticeText.Contains("wasm-bindgen runtime"));

// --- 18f. Web accept / decoder parity ---
var webRoot = RepoRoot();
var htmlText = webRoot is null ? "" : File.ReadAllText(Path.Combine(webRoot, "web", "OrmPacker.Web", "wwwroot", "index.html"));
var jsText = webRoot is null ? "" : File.ReadAllText(Path.Combine(webRoot, "web", "OrmPacker.Web", "wwwroot", "js", "interop.js"));
var acceptStart = htmlText.IndexOf("accept=\"", StringComparison.Ordinal);
var acceptEnd = acceptStart < 0 ? -1 : htmlText.IndexOf('"', acceptStart + 8);
var accepted = acceptStart < 0 || acceptEnd < 0
    ? new HashSet<string>()
    : htmlText.Substring(acceptStart + 8, acceptEnd - acceptStart - 8)
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim().TrimStart('.').ToLowerInvariant()).ToHashSet();
var supportedLine = jsText.Split('\n').FirstOrDefault(l => l.Contains("const SUPPORTED =")) ?? "";
var regexMarker = "/\\.(";
var regexStart = supportedLine.IndexOf(regexMarker, StringComparison.Ordinal);
var regexEnd = regexStart < 0 ? -1 : supportedLine.IndexOf(")$/i", regexStart, StringComparison.Ordinal);
var supported = new HashSet<string>();
if (regexStart >= 0 && regexEnd > regexStart)
{
    foreach (var token in supportedLine.Substring(regexStart + regexMarker.Length,
                 regexEnd - regexStart - regexMarker.Length).Split('|'))
    {
        var optional = token.IndexOf('?');
        if (optional <= 0)
        {
            supported.Add(token);
            continue;
        }
        supported.Add(token.Remove(optional - 1, 2)); // jpe?g -> jpg
        supported.Add(token.Replace("?", ""));        // jpe?g -> jpeg
    }
}
Check("web accept matches interop SUPPORTED",
    accepted.Count > 0 && accepted.SetEquals(supported),
    $"accept=[{string.Join(",", accepted.OrderBy(x => x))}] supported=[{string.Join(",", supported.OrderBy(x => x))}]");
Check("web does not advertise formats it cannot decode",
    !accepted.Contains("tif") && !accepted.Contains("tiff") && !accepted.Contains("ico") &&
    accepted.Contains("webp"));
var faviconPath = Path.Combine(webRoot ?? "", "web", "OrmPacker.Web", "wwwroot", "favicon.png");
Check("web index.html references the bundled icon",
    htmlText.Contains("rel=\"icon\"") && htmlText.Contains("href=\"favicon.png\"") &&
    File.Exists(faviconPath));

// --- 19. Documented behavior: arguments, normal maps, auto-invert, Ctrl+S ---
Exception? regressionError = null;
var regressionThread = new Thread(() =>
{
    try
    {
        var pinRough = SolidPng(Path.Combine(dir, "pin_Rock_Roughness.png"), 4, 4, 120, 120, 120);
        var pinNormal = SolidPng(Path.Combine(dir, "pin_Rock_Normal.png"), 4, 4, 128, 128, 255);
        using var form = NewForm([pinRough, pinNormal]);

        var slots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        object Slot(int index) => slots.GetValue(index)!;
        object? SlotField(int index, string name) => Slot(index).GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(Slot(index));
        var files = (System.Collections.ICollection)typeof(MainForm)
            .GetField("_files", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

        Check("launch arguments load files", files.Count == 2, $"files={files.Count}");
        var assigned = Enumerable.Range(0, 4).Count(i => SlotField(i, "Image") is not null);
        Check("normal map is not assigned to a slot", assigned == 1, $"assigned={assigned}");
        Check("Roughness goes to G by name without inversion",
            SlotField(1, "Image") is not null && SlotField(1, "InvertValue") is false);

        var presetBox = (ModernComboBox)typeof(MainForm)
            .GetField("_presetBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        presetBox.SelectedItem = Preset.UnityHdrp;
        Check("auto-invert when changing preset",
            SlotField(3, "Image") is not null && SlotField(3, "InvertValue") is true,
            $"A image={SlotField(3, "Image") is not null}, invert={SlotField(3, "InvertValue")}");
        presetBox.SelectedItem = Preset.Orm;
        Check("switching back to ORM clears inversion",
            SlotField(1, "Image") is not null && SlotField(1, "InvertValue") is false);

        var pinSmooth = SolidPng(Path.Combine(dir, "pin_Rock_Smoothness.png"), 4, 4, 100, 100, 100);
        using var smoothForm = NewForm([pinSmooth]);
        var smoothSlots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(smoothForm)!;
        object? SmoothField(int index, string name) => smoothSlots.GetValue(index)!.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(smoothSlots.GetValue(index));
        Check("Smoothness → Roughness inverted on load",
            SmoothField(1, "Image") is not null && SmoothField(1, "InvertValue") is true);

        var saved = false;
        typeof(MainForm).GetProperty("SaveAction", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(form, new Action(() => saved = true));
        var keys = new KeyEventArgs(Keys.Control | Keys.S);
        typeof(MainForm).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, new object[] { keys });
        Check("Ctrl+S triggers save", saved && keys.SuppressKeyPress);
    }
    catch (Exception ex) { regressionError = ex; }
});
regressionThread.SetApartmentState(ApartmentState.STA);
regressionThread.Start();
regressionThread.Join();
Check("documented behavior works without exceptions", regressionError is null, regressionError?.Message ?? "");

// --- 19b. Release artifacts ---
var releaseRoot = RepoRoot();
var buildProps = releaseRoot is null ? "" :
    File.ReadAllText(Path.Combine(releaseRoot, "Directory.Build.props"));
var releaseWorkflow = releaseRoot is null ? "" :
    File.ReadAllText(Path.Combine(releaseRoot, ".github", "workflows", "release.yml"));
Check("release version and checksums are configured",
    buildProps.Contains("<Version>1.0.0</Version>") &&
    releaseWorkflow.Contains("SHA256SUMS.txt"));
Check("release attaches exactly two archives and one checksum file",
    releaseWorkflow.Contains("publish/**/orm-packer-*.zip") &&
    releaseWorkflow.Contains("publish/SHA256SUMS.txt") &&
    !releaseWorkflow.Contains("publish/**/SHA256SUMS.txt"));
var buildScriptPath = Path.Combine(releaseRoot ?? "", "build.ps1");
Check("single build entry point puts all outputs in publish/",
    File.Exists(buildScriptPath) &&
    File.ReadAllText(buildScriptPath).Contains("Remove-StaleBuilds") &&
    File.ReadAllText(buildScriptPath).Contains("Join-Path $root 'publish'"));

// --- 20. Convert mode: channel swizzle between presets ---
static TextureImage RoundTrip(string path, PackedTexture texture)
{
    Packer.SavePng(texture, path, true);
    return TextureImage.Load(path);
}
var hdrpOriginal = Packer.Pack(2, 2,
    new ChannelInput(null, SourceComponent.Red, 30, false),
    new ChannelInput(null, SourceComponent.Red, 10, false),
    new ChannelInput(null, SourceComponent.Red, 200, false),
    new ChannelInput(null, SourceComponent.Red, 40, false), true);
var hdrpSource = RoundTrip(Path.Combine(dir, "swizzle_hdrp.png"), hdrpOriginal);
var swizzledOrm = PackerConvert.Convert(hdrpSource, Preset.UnityHdrp, Preset.Orm);
Check("convert HDRP → ORM: R=AO, G=inverted Smoothness, B=Metallic, A=solid",
    swizzledOrm.Pixels[2] == 10 && swizzledOrm.Pixels[1] == 215 &&
    swizzledOrm.Pixels[0] == 30 && swizzledOrm.Pixels[3] == 255,
    $"R={swizzledOrm.Pixels[2]}, G={swizzledOrm.Pixels[1]}, B={swizzledOrm.Pixels[0]}, A={swizzledOrm.Pixels[3]}");
var ormSource = RoundTrip(Path.Combine(dir, "swizzle_orm.png"), swizzledOrm);
var swizzledBack = PackerConvert.Convert(ormSource, Preset.Orm, Preset.UnityHdrp);
Check("convert ORM → HDRP with inverse mapping",
    swizzledBack.Pixels[2] == 30 && swizzledBack.Pixels[1] == 10 &&
    swizzledBack.Pixels[3] == 40,
    $"R={swizzledBack.Pixels[2]}, G={swizzledBack.Pixels[1]}, A={swizzledBack.Pixels[3]}");
var swizzledUrp = PackerConvert.Convert(hdrpSource, Preset.UnityHdrp, Preset.UnityUrp);
Check("convert HDRP → URP: R=G=B=Metallic, A=Smoothness",
    swizzledUrp.Pixels[0] == 30 && swizzledUrp.Pixels[1] == 30 &&
    swizzledUrp.Pixels[2] == 30 && swizzledUrp.Pixels[3] == 40);
Check("URP preset per spec: R=G=B=Metallic, A=Smoothness",
    Preset.UnityUrp.Channels[Channel.R].Kind == MapKind.Metallic &&
    Preset.UnityUrp.Channels[Channel.G].Kind == MapKind.Metallic &&
    Preset.UnityUrp.Channels[Channel.B].Kind == MapKind.Metallic &&
    Preset.UnityUrp.Channels[Channel.A].Kind == MapKind.Smoothness);

// Mappings are the single source of truth for both the UI panel and BuildInputs.
var hdrpToOrm = PackerConvert.BuildMappings(Preset.UnityHdrp, Preset.Orm);
Check("mapping HDRP→ORM: R←G, G←A inverted, B←R, A←constant",
    hdrpToOrm.Count == 4 &&
    hdrpToOrm[0] == new PackerConvert.ConvertMapping(Channel.R, Channel.G, false) &&
    hdrpToOrm[1] == new PackerConvert.ConvertMapping(Channel.G, Channel.A, true) &&
    hdrpToOrm[2] == new PackerConvert.ConvertMapping(Channel.B, Channel.R, false) &&
    hdrpToOrm[3].IsConstant,
    string.Join(", ", hdrpToOrm));
Check("mapping matches BuildInputs channels",
    PackerConvert.BuildInputs(hdrpSource, Preset.UnityHdrp, Preset.Orm)
        .Select((input, i) => (input.Source, input.Invert) ==
            (hdrpToOrm[i].Source is null ? SourceComponent.Red : (SourceComponent)hdrpToOrm[i].Source!.Value,
             hdrpToOrm[i].Invert)).All(same => same));

// --- 21. Convert mode in the UI ---
Exception? convertUiError = null;
var convertThread = new Thread(() =>
{
    try
    {
        var sourcePath = Path.Combine(dir, "swizzle_hdrp.png");
        using var form = NewForm();
        var modeBox = (ModernComboBox)typeof(MainForm)
            .GetField("_modeBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        modeBox.SelectedIndex = 1; // Convert
        var fromBox = (ModernComboBox)typeof(MainForm)
            .GetField("_fromPresetBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        fromBox.SelectedItem = Preset.UnityHdrp;
        var presetBox = (ModernComboBox)typeof(MainForm)
            .GetField("_presetBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        presetBox.SelectedItem = Preset.Orm;
        typeof(MainForm).GetField("_convertSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(form, TextureImage.Load(sourcePath));

        var packMethod = typeof(MainForm).GetMethod("Pack", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var result = (PackedTexture)packMethod.Invoke(form, new object[] { 2, 2 })!;
        Check("UI convert: HDRP → ORM",
            result.Pixels[2] == 10 && result.Pixels[1] == 215 && result.Pixels[0] == 30,
            $"R={result.Pixels[2]}, G={result.Pixels[1]}, B={result.Pixels[0]}");
        var nameBox = (Control)typeof(MainForm)
            .GetField("_nameBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        typeof(MainForm).GetMethod("UpdateName", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, null);
        Check("UI convert: name has preset suffix", nameBox.Text.Contains("_ORM"), nameBox.Text);
    }
    catch (Exception ex) { convertUiError = ex; }
});
convertThread.SetApartmentState(ApartmentState.STA);
convertThread.Start();
convertThread.Join();
Check("UI convert without exceptions", convertUiError is null, convertUiError?.Message ?? "");

// --- 21b. Convert UX: source card, hidden slots, mapping, steps, status ---
Exception? convertUxError = null;
var convertUxThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        // Visible also considers invisible parents; this test checks the control's own flag.
        bool OwnVisible(Control control) => (bool)typeof(Control)
            .GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(control, new object[] { 2 })!;
        var slotsHost = Field("_slotsHost");
        var convertHost = Field("_convertHost");
        var sourceCard = Field("_sourceCard");
        var modeBox = (ModernComboBox)Field("_modeBox");
        var fromBox = (ModernComboBox)Field("_fromPresetBox");
        var presetBox = (ModernComboBox)Field("_presetBox");
        ((ModernComboBox)Field("_sizeBox")).SelectedItem = OutputSize.Original;
        var status = Field("_statusLabel");
        var steps = (Array)typeof(MainForm).GetField("_stepLabels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        var mapping = (Array)typeof(MainForm).GetField("_mappingLabels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        var badges = (Array)typeof(MainForm).GetField("_mappingBadges", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;

        Check("Pack: slots visible, convert panel hidden",
            OwnVisible(slotsHost) && !OwnVisible(convertHost));
        modeBox.SelectedIndex = 1;
        Check("Convert: slots hidden, source card visible",
            !OwnVisible(slotsHost) && OwnVisible(convertHost) && OwnVisible(sourceCard));
        Check("Convert: empty-card hint is visible",
            Descendants(sourceCard).OfType<Label>()
                .Any(l => l.Text == Localization.Get("convert.dropHint") && OwnVisible(l)));

        // The full-size hint label swallows the parent's click. Check that every child
        // carries a forwarding handler without raising it (the real handler opens a modal dialog).
        var hintLabel = Descendants(sourceCard).OfType<Label>()
            .First(l => l.Text == Localization.Get("convert.dropHint"));
        var clickKey = typeof(Control).GetField("s_clickEvent", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var eventsProperty = typeof(Control).GetProperty("Events", BindingFlags.NonPublic | BindingFlags.Instance)!;
        bool ForwardsClick(Control control)
        {
            var list = (EventHandlerList?)eventsProperty.GetValue(control);
            return list?[clickKey]?.GetInvocationList()
                .Any(d => ReferenceEquals(d.Target, sourceCard)) == true;
        }
        Check("Convert: empty-card hint click opens the file picker", ForwardsClick(hintLabel));
        Check("Convert: source card click opens the file picker", ForwardsClick(sourceCard));

        var slots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        Check("Convert: slot warnings are not drawn",
            Enumerable.Range(0, 4).All(i =>
                !Descendants((Control)slots.GetValue(i)!).OfType<Label>()
                    .Any(l => l.Text.Contains(Localization.Get("slot.warn.Empty"), StringComparison.Ordinal))));

        fromBox.SelectedItem = Preset.UnityHdrp;
        presetBox.SelectedItem = Preset.Orm;
        Check("Convert: paired drop-down is synced with the target preset",
            ReferenceEquals(((ModernComboBox)Field("_convertToPresetBox")).SelectedItem, Preset.Orm));

        var addFiles = typeof(MainForm).GetMethod("AddFiles", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var first = Path.Combine(dir, "swizzle_hdrp.png");
        var second = Path.Combine(dir, "swizzle_orm.png");
        addFiles.Invoke(form, new object?[] { new[] { first, second }, null });
        Check("Convert: multiple files use the first, rest reported in status",
            status.Text.Contains("1 map") && status.Text.Contains("HDRP → ORM") &&
            status.Text.Contains(Localization.Get("convert.extraFiles").Replace("{0}", "2").Replace("{1}", "swizzle_hdrp.png")),
            status.Text);
        var cardFileName = Descendants(sourceCard).OfType<Label>()
            .First(l => l.Text == "swizzle_hdrp.png");
        Check("Convert: loaded-card labels stay clickable", ForwardsClick(cardFileName));
        Check("Convert: step 1 done after drop",
            ((Label)steps.GetValue(0)!).Text.StartsWith("✓", StringComparison.Ordinal));
        Check("Convert: step 2 done for different presets",
            ((Label)steps.GetValue(1)!).Text.StartsWith("✓", StringComparison.Ordinal));
        Check("Convert: step 3 waits for an actual save",
            !((Label)steps.GetValue(2)!).Text.StartsWith("✓", StringComparison.Ordinal));
        typeof(MainForm).GetField("_savedInSession", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(form, true);
        typeof(MainForm).GetMethod("UpdateSteps", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, null);
        Check("Convert: step 3 highlights after save",
            ((Label)steps.GetValue(2)!).Text.StartsWith("✓", StringComparison.Ordinal));

        Check("Convert: mapping panel comes from Core",
            ((Label)mapping.GetValue(0)!).Text.Contains("Occlusion") &&
            ((Label)mapping.GetValue(1)!).Text.Contains("Smoothness") &&
            OwnVisible((Label)badges.GetValue(1)!) && !OwnVisible((Label)badges.GetValue(0)!),
            string.Join(" | ", mapping.Cast<Label>().Select(l => l.Text)));

        var previewInfo = Field("_previewInfo");
        Check("Convert: readable output header",
            previewInfo.Text == string.Format(CultureInfo.InvariantCulture,
                Localization.Get("preview.header"), "2", "2", "RGB", Localization.Get("bits.8"), "PNG"),
            previewInfo.Text);

        fromBox.SelectedItem = Preset.Orm;
        Check("Convert: matching presets are highlighted",
            OwnVisible((Control)Field("_samePresetHint")));
        Check("Convert: step 2 undone when presets match",
            !((Label)steps.GetValue(1)!).Text.StartsWith("✓", StringComparison.Ordinal));

        // Pack: the counter tracks loaded maps only, not conversion.
        using var packForm = NewForm([
            Path.Combine(dir, "T_Rock_AO.png"), Path.Combine(dir, "T_Rock_Roughness.png")
        ]);
        var packStatus = (Control)typeof(MainForm)
            .GetField("_statusLabel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(packForm)!;
        Check("Pack: status counts maps", packStatus.Text.Contains("2 maps"), packStatus.Text);
    }
    catch (Exception ex) { convertUxError = ex; }
});
convertUxThread.SetApartmentState(ApartmentState.STA);
convertUxThread.Start();
convertUxThread.Join();
Check("Convert UX without exceptions", convertUxError is null, convertUxError?.Message ?? "");

// --- 21b2. Unified output header in Pack and Convert ---
Exception? outputHeaderError = null;
var outputHeaderThread = new Thread(() =>
{
    try
    {
        using var form = NewForm([Path.Combine(dir, "T_Rock_AO.png")]);
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        ((ModernComboBox)Field("_sizeBox")).SelectedItem = OutputSize.Original;
        ((ModernComboBox)Field("_presetBox")).SelectedItem = Preset.UnityHdrp;
        var info = Field("_previewInfo");
        Check("Pack: RGBA output header for an alpha preset",
            info.Text == string.Format(CultureInfo.InvariantCulture, Localization.Get("preview.header"),
                "2", "2", Localization.Get("preview.rgba"), Localization.Get("bits.8"), "PNG"),
            info.Text);

        typeof(MainForm).GetMethod("SetPreviewMode", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, new object[]
            {
                Enum.Parse(typeof(MainForm).GetNestedType("PreviewMode", BindingFlags.NonPublic)!, "R")
            });
        Check("header shows output channels, not the selected preview tab",
            info.Text.Contains(Localization.Get("preview.rgba")), info.Text);

        ((ModernComboBox)Field("_formatBox")).SelectedItem =
            OutputFormat.All.First(f => f.Format == SaveFormat.Jpeg);
        Check("header reflects a format without alpha",
            info.Text == string.Format(CultureInfo.InvariantCulture, Localization.Get("preview.header"),
                "2", "2", Localization.Get("preview.rgb"), Localization.Get("bits.8"), "JPEG"),
            info.Text);
    }
    catch (Exception ex) { outputHeaderError = ex; }
});
outputHeaderThread.SetApartmentState(ApartmentState.STA);
outputHeaderThread.Start();
outputHeaderThread.Join();
Check("output header without exceptions", outputHeaderError is null, outputHeaderError?.Message ?? "");

// --- 21b3. Convert without source: empty name, disabled unpack, step 3 off ---
Exception? emptySourceError = null;
var emptySourceThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        ((ModernComboBox)Field("_modeBox")).SelectedIndex = 1;
        var nameBox = (TextBox)Field("_nameBox");
        var steps = (Array)typeof(MainForm).GetField("_stepLabels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        var unpack = (ModernButton)Field("_unpackButton");

        Check("Convert without map: empty name with placeholder",
            nameBox.Text.Length == 0 && nameBox.PlaceholderText == Localization.Get("filename.placeholder"),
            nameBox.Text + " / " + nameBox.PlaceholderText);
        Check("Convert without map: step 3 is not highlighted",
            !((Label)steps.GetValue(2)!).Text.StartsWith("✓", StringComparison.Ordinal));
        Check("Convert without map: unpack is disabled", !unpack.Enabled);
        Check("Convert without map: status is conversion-specific",
            ((Control)Field("_statusLabel")).Text == Localization.Get("convert.status.empty"),
            ((Control)Field("_statusLabel")).Text);

        typeof(MainForm).GetMethod("AddFiles", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, new object?[] { new[] { Path.Combine(dir, "swizzle_hdrp.png") }, null });
        Check("Convert with map: name from file, step 3 waits for save, unpack enabled",
            nameBox.Text == "swizzle_hdrp_ORM.png" &&
            !((Label)steps.GetValue(2)!).Text.StartsWith("✓", StringComparison.Ordinal) && unpack.Enabled,
            nameBox.Text);

        typeof(MainForm).GetMethod("ClearSlots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, null);
        Check("Convert after clear: name and step 3 are reset",
            nameBox.Text.Length == 0 &&
            !((Label)steps.GetValue(2)!).Text.StartsWith("✓", StringComparison.Ordinal) && !unpack.Enabled);
    }
    catch (Exception ex) { emptySourceError = ex; }
});
emptySourceThread.SetApartmentState(ApartmentState.STA);
emptySourceThread.Start();
emptySourceThread.Join();
Check("convert empty source without exceptions", emptySourceError is null, emptySourceError?.Message ?? "");

// --- 21b4. Inline inversion badge next to the source channel ---
Exception? badgeError = null;
var badgeThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        ((ModernComboBox)Field("_modeBox")).SelectedIndex = 1;
        ((ModernComboBox)Field("_fromPresetBox")).SelectedItem = Preset.UnityHdrp;
        ((ModernComboBox)Field("_presetBox")).SelectedItem = Preset.Orm;
        form.ClientSize = new Size(form.MinimumSize.Width, form.MinimumSize.Height);
        var mapping = (Array)typeof(MainForm).GetField("_mappingLabels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        var badges = (Array)typeof(MainForm).GetField("_mappingBadges", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;

        foreach (var language in new[] { "ru", "en" })
        {
            Localization.SetLanguage(language);
            Application.DoEvents();
            var label = (Label)mapping.GetValue(1)!;
            var badge = (Label)badges.GetValue(1)!;
            var textEnd = label.Left + TextRenderer.MeasureText(label.Text, label.Font).Width;
            var ownVisible = (bool)typeof(Control)
                .GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(badge, new object[] { 2 })!;
            Check($"inline inversion badge: {language}",
                ownVisible && badge.Left >= textEnd && badge.Left <= textEnd + 12 && badge.Right <= 520,
                $"textEnd={textEnd}, badge={badge.Left}");
        }
        Localization.SetLanguage("en");
    }
    catch (Exception ex) { badgeError = ex; }
});
badgeThread.SetApartmentState(ApartmentState.STA);
badgeThread.Start();
badgeThread.Join();
Check("inversion badge without exceptions", badgeError is null, badgeError?.Message ?? "");

// --- 21c. Live localization: re-binding controls after repeated toggles ---
Exception? localizationUiError = null;
var localizationThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        (string Key, Func<string> Text)[] bound =
        [
            ("app.subtitle", () => Field("_subtitle").Text),
            ("header.preset", () => Field("_presetLabel").Text),
            ("header.size", () => Field("_sizeLabel").Text),
            ("convert.from", () => Field("_fromPresetLabel").Text),
            ("convert.to", () => Field("_convertToLabel").Text),
            ("convert.mapping", () => Field("_mappingTitle").Text),
            ("preview.output", () => Field("_outputTitle").Text),
            ("filename.label", () => Field("_nameLabel").Text),
            ("format.label", () => Field("_formatLabel").Text),
            ("save.button", () => Field("_saveButton").Text),
            ("preview.fit", () => Field("_fitButton").Text),
            ("preview.hint", () => Field("_previewHint").Text),
            ("lang.ru", () => Field("_langRu").Text),
            ("lang.en", () => Field("_langEn").Text)
        ];
        var langRu = (ModernButton)Field("_langRu");
        var langEn = (ModernButton)Field("_langEn");
        var modeBox = (ModernComboBox)Field("_modeBox");
        var modeList = (ListBox)typeof(ModernComboBox)
            .GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(modeBox)!;
        var viewButtons = (System.Collections.IDictionary)typeof(MainForm)
            .GetField("_viewButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var openDialog = (OpenFileDialog)typeof(MainForm)
            .GetField("_openDialog", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var slots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;

        // Toggle RU → EN → RU → EN; SetLanguage must notify Changed subscribers.
        foreach (var language in new[] { "ru", "en", "ru", "en" })
        {
            Localization.SetLanguage(language);
            var all = bound.All(pair => pair.Text() == Localization.Get(pair.Key));
            Check($"controls localized: {language}", all,
                string.Join(" | ", bound.Where(p => p.Text() != Localization.Get(p.Key))
                    .Select(p => $"{p.Key}={p.Text()}")));
            Check($"language toggle highlights the current language: {language}",
                langRu.Selected == (language == "ru") && langEn.Selected == (language == "en"));
            Check($"preview tabs localized: {language}",
                viewButtons.Values.Cast<Button>().All(b =>
                    b.Text == Localization.Get("preview." + b.Text.ToLowerInvariant())));
            Check($"modes and dialog localized: {language}",
                (string)modeBox.Items[0]! == Localization.Get("mode.pack") &&
                (string)modeBox.Items[1]! == Localization.Get("mode.convert") &&
                (string)modeBox.SelectedItem! == (string)modeBox.Items[modeBox.SelectedIndex]! &&
                openDialog.Title == Localization.Get("dialog.open.title") &&
                Descendants((Control)slots.GetValue(0)!).OfType<ModernCheckBox>()
                    .Any(c => c.Text == Localization.Get("slot.invert")));
            typeof(ModernComboBox).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(modeBox, new object[] { EventArgs.Empty });
            Check($"mode drop-down: both items repainted: {language}",
                modeList.Items.Count == 2 &&
                (string)modeList.Items[0]! == Localization.Get("mode.pack") &&
                (string)modeList.Items[1]! == Localization.Get("mode.convert"));
        }
        Localization.SetLanguage("en");
    }
    catch (Exception ex) { localizationUiError = ex; }
});
localizationThread.SetApartmentState(ApartmentState.STA);
localizationThread.Start();
localizationThread.Join();
Check("live localization without exceptions", localizationUiError is null, localizationUiError?.Message ?? "");

// --- 21d. Labels do not wrap at minimum width ---
Exception? layoutError = null;
var layoutThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);

        foreach (var language in new[] { "ru", "en" })
        {
            Localization.SetLanguage(language);
            form.ClientSize = new Size(form.MinimumSize.Width, form.MinimumSize.Height);
            Application.DoEvents();

            var fit = (ModernButton)Field("_fitButton");
            var fitText = graphics.MeasureString(fit.Text, fit.Font);
            Check($"Fit button without wrapping: {language}", fit.Width >= fitText.Width + 8,
                $"button={fit.Width}, text={fitText.Width:F1}");

            var buttonsFit = Descendants(form).OfType<ModernButton>()
                .All(b => graphics.MeasureString(b.Text, b.Font).Width <= b.Width - 6);
            Check($"buttons not clipped: {language}", buttonsFit,
                string.Join(", ", Descendants(form).OfType<ModernButton>()
                    .Where(b => graphics.MeasureString(b.Text, b.Font).Width > b.Width - 6)
                    .Select(b => $"{b.Text} {graphics.MeasureString(b.Text, b.Font).Width:F0}>{b.Width - 6}")));

            var presetBox = (Control)Field("_presetBox");
            var sizeBox = (Control)Field("_sizeBox");
            var modeBox = (Control)Field("_modeBox");
            var presetLabel = (Control)Field("_presetLabel");
            var nameBox = (Control)Field("_nameBox");
            var formatLabel = (Control)Field("_formatLabel");
            var formatBox = (Control)Field("_formatBox");
            var saveButton = (Control)Field("_saveButton");
            var packButton = (Control)Field("_packModeButton");
            var convertButton = (Control)Field("_convertModeButton");
            Check($"settings rows do not overlap: {language}",
                presetBox.Bottom <= sizeBox.Top && sizeBox.Bottom <= packButton.Top &&
                packButton.Right <= convertButton.Left && presetLabel.Right <= presetBox.Left);
            var nameLabel = (Control)Field("_nameLabel");
            var unpackButton = (Control)Field("_unpackFooterButton");
            var clearButton = (Control)Field("_clearButton");
            Check($"settings do not overlap: {language}",
                formatLabel.Right <= formatBox.Left && nameLabel.Right <= nameBox.Left &&
                saveButton.Right <= unpackButton.Left && unpackButton.Right <= clearButton.Left,
                $"format={formatLabel.Right}/{formatBox.Left}, name={nameLabel.Right}/{nameBox.Left}, " +
                $"buttons={saveButton.Right}/{unpackButton.Left}/{unpackButton.Right}/{clearButton.Left}");
        }
        Localization.SetLanguage("en");
        form.ClientSize = new Size(1000, 860);
        Application.DoEvents();
        Check("settings controls stay in place on resize",
            ((Control)Field("_saveButton")).Left == 12);
    }
    catch (Exception ex) { layoutError = ex; }
});
layoutThread.SetApartmentState(ApartmentState.STA);
layoutThread.Start();
layoutThread.Join();
Check("labels without exceptions", layoutError is null, layoutError?.Message ?? "");

// --- 21e. Slot meta row survives the minimum window width ---
Exception? slotMetaError = null;
var slotMetaThread = new Thread(() =>
{
    try
    {
        var longName = "T_VeryLongMaterialName_ThatShouldBeEllipsized_WithoutHidingTheMetaRow_AO.png";
        var path = SolidPng(Path.Combine(dir, longName), 8, 6, 30, 40, 50);
        using var form = NewForm([path]);
        form.ClientSize = new Size(form.MinimumSize.Width, form.MinimumSize.Height);
        Application.DoEvents();

        var slots = (Array)typeof(MainForm)
            .GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var slot = slots.GetValue(0)!;
        Control SlotLabel(string name) => (Control)slot.GetType()
            .GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(slot)!;
        var info = (Label)SlotLabel("Info");
        var meta = (Label)SlotLabel("Meta");
        var host = meta.Parent!;

        Check("slot file name ellipsizes", info.AutoEllipsis &&
            info.Text.Contains("VeryLongMaterialName", StringComparison.OrdinalIgnoreCase),
            info.Text);
        // A WinForms control reports Visible=false while its form is not shown,
        // so this checks the rendered text and geometry instead of the Visible flag.
        Check("slot meta keeps size, bit depth and extension",
            meta.Text == $"8×6 · {Localization.Get("bits.8")} · PNG" && meta.Height > 0,
            meta.Text);
        Check("slot meta fits inside the slot at minimum width",
            meta.Right <= host.Right && meta.Bottom <= host.Height && info.Right <= info.Parent!.Width,
            $"meta={meta.Bounds}, info={info.Bounds}, host={host.ClientSize}");
        var metaTextWidth = TextRenderer.MeasureText(meta.Text, meta.Font).Width;
        Check("slot meta text is not clipped at minimum width", meta.Width >= metaTextWidth,
            $"label={meta.Width}, text={metaTextWidth}");
    }
    catch (Exception ex) { slotMetaError = ex; }
});
slotMetaThread.SetApartmentState(ApartmentState.STA);
slotMetaThread.Start();
slotMetaThread.Join();
Check("slot meta layout without exceptions", slotMetaError is null, slotMetaError?.Message ?? "");

// --- 22. Session and output name ---
var sessionDir = Path.Combine(dir, "session");
Directory.CreateDirectory(sessionDir);
var sessionPath = Path.Combine(sessionDir, "session.json");
var savedSession = new SessionState
{
    PresetId = "hdrp", SizePixels = 512, FormatId = "tga",
    Slots = new[]
    {
        new SlotState(Path.Combine(dir, "T_Rock_AO.png"), SourceComponent.Green, 77, true),
        new SlotState(null, SourceComponent.Red, 12, false),
        new SlotState(null, SourceComponent.Red, 13, false),
        new SlotState(null, SourceComponent.Red, 14, false)
    },
    RecentPaths = new List<string> { "a.png", "b.png" },
    LastOutputDirectory = sessionDir
};
SessionStore.Save(sessionPath, savedSession);
var loadedSession = SessionStore.Load(sessionPath);
Check("session: preset/size/format",
    loadedSession.PresetId == "hdrp" && loadedSession.SizePixels == 512 && loadedSession.FormatId == "tga");
Check("session: slot settings and paths",
    loadedSession.Slots[0].Path == Path.Combine(dir, "T_Rock_AO.png") &&
    loadedSession.Slots[0].Source == SourceComponent.Green &&
    loadedSession.Slots[0].SolidValue == 77 && loadedSession.Slots[0].Invert &&
    loadedSession.RecentPaths.SequenceEqual(["a.png", "b.png"]));
Check("session: broken JSON does not crash",
    SessionStore.PresetById(SessionStore.Load(WriteBrokenJson(Path.Combine(sessionDir, "broken.json"))).PresetId) == Preset.Orm);
static string WriteBrokenJson(string path) { File.WriteAllText(path, "{not json"); return path; }

var dupDir = Path.Combine(dir, "naming");
Directory.CreateDirectory(dupDir);
foreach (var stale in Directory.GetFiles(dupDir, "dup*.png")) File.Delete(stale);
File.WriteAllBytes(Path.Combine(dupDir, "dup.png"), [1]);
Check("name collision: _01", OutputNaming.EnsureUnique("dup.png", dupDir) == "dup_01.png");
File.WriteAllBytes(Path.Combine(dupDir, "dup_01.png"), [1]);
Check("name collision: _02", OutputNaming.EnsureUnique("dup.png", dupDir) == "dup_02.png");
Check("Custom without suffix", Preset.Custom.Suffix == "" &&
    OutputNaming.Suggest(["T_Rock_AO.png"], Preset.Custom.Suffix) == "T_Rock.png");

Exception? sessionUiError = null;
var sessionThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        typeof(MainForm).GetProperty("SessionPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(form, sessionPath);
        typeof(MainForm).GetMethod("ApplySession", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, new object[] { SessionStore.Load(sessionPath), false });
        var presetBox = (ModernComboBox)typeof(MainForm)
            .GetField("_presetBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var sizeBox = (ModernComboBox)typeof(MainForm)
            .GetField("_sizeBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var slots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        var firstSlot = slots.GetValue(0)!;
        bool SlotFlag(string name) => (bool)firstSlot.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(firstSlot)!;
        Check("UI: session restored preset/size",
            ReferenceEquals(presetBox.SelectedItem, Preset.UnityHdrp) &&
            ((OutputSize)sizeBox.SelectedItem!).Pixels == 512);
        Check("UI: session restored slot",
            SlotFlag("InvertValue") && (byte)firstSlot.GetType()
                .GetField("SolidValue", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(firstSlot)! == 77);
        typeof(MainForm).GetMethod("PersistSession", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, null);
        Check("UI: session is saved", SessionStore.Load(sessionPath).PresetId == "hdrp");
    }
    catch (Exception ex) { sessionUiError = ex; }
});
sessionThread.SetApartmentState(ApartmentState.STA);
sessionThread.Start();
sessionThread.Join();
Check("UI session without exceptions", sessionUiError is null, sessionUiError?.Message ?? "");

// --- 22b. Session remembers the mode and preset pair ---
Exception? convertSessionError = null;
var convertSessionThread = new Thread(() =>
{
    try
    {
        var convertSessionPath = Path.Combine(sessionDir, "convert-session.json");
        var convertSession = new SessionState
        {
            PresetId = "orm", FromPresetId = "hdrp", ConvertMode = true,
            ConvertSourcePath = Path.Combine(dir, "swizzle_hdrp.png")
        };
        SessionStore.Save(convertSessionPath, convertSession);
        var reloaded = SessionStore.Load(convertSessionPath);
        Check("session: mode and preset pair in JSON",
            reloaded.ConvertMode && reloaded.FromPresetId == "hdrp" &&
            reloaded.ConvertSourcePath == convertSession.ConvertSourcePath);
        var legacyPath = Path.Combine(sessionDir, "legacy.json");
        File.WriteAllText(legacyPath, "{\"PresetId\":\"orm\",\"SizePixels\":1024,\"FormatId\":\"png\"}");
        var legacy = SessionStore.Load(legacyPath);
        Check("session: legacy JSON without new fields — Pack",
            !legacy.ConvertMode && legacy.FromPresetId is null && legacy.ConvertSourcePath is null);

        using var form = NewForm();
        typeof(MainForm).GetProperty("SessionPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(form, convertSessionPath);
        typeof(MainForm).GetMethod("ApplySession", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, new object[] { reloaded, false });
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var modeBox = (ModernComboBox)Field("_modeBox");
        var fromBox = (ModernComboBox)Field("_fromPresetBox");
        var source = typeof(MainForm).GetField("_convertSource", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form);
        Check("UI: session restored Convert and the From/Target pair",
            modeBox.SelectedIndex == 1 && ReferenceEquals(fromBox.SelectedItem, Preset.UnityHdrp) &&
            ReferenceEquals(((ModernComboBox)Field("_presetBox")).SelectedItem, Preset.Orm) &&
            source is not null);

        typeof(MainForm).GetMethod("PersistSession", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, null);
        var saved = SessionStore.Load(convertSessionPath);
        Check("UI: session saved mode/pair/source",
            saved.ConvertMode && saved.FromPresetId == "hdrp" &&
            saved.ConvertSourcePath == convertSession.ConvertSourcePath);
    }
    catch (Exception ex) { convertSessionError = ex; }
});
convertSessionThread.SetApartmentState(ApartmentState.STA);
convertSessionThread.Start();
convertSessionThread.Join();
Check("Convert session without exceptions", convertSessionError is null, convertSessionError?.Message ?? "");

// --- 23. Hotkeys and drag & drop ---
Exception? hotkeyError = null;
var hotkeyThread = new Thread(() =>
{
    try
    {
        using var form = NewForm([Path.Combine(dir, "T_Rock_AO.png")]);
        var onKeyDown = typeof(MainForm).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var presetBox = (ModernComboBox)typeof(MainForm)
            .GetField("_presetBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var digit = new KeyEventArgs(Keys.D2);
        onKeyDown.Invoke(form, new object[] { digit });
        Check("hotkey 1–4: preset switch",
            digit.SuppressKeyPress && ReferenceEquals(presetBox.SelectedItem, Preset.UnityHdrp));

        var slots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        var clear = new KeyEventArgs(Keys.Control | Keys.K);
        onKeyDown.Invoke(form, new object[] { clear });
        var cleared = Enumerable.Range(0, 4).All(i => slots.GetValue(i)!.GetType()
            .GetField("Image", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
            .GetValue(slots.GetValue(i)) is null);
        Check("Ctrl+K clears slots", clear.SuppressKeyPress && cleared);
        Check("drag & drop enabled for form and slots",
            form.AllowDrop && Enumerable.Range(0, 4).All(i => ((Control)slots.GetValue(i)!).AllowDrop));
    }
    catch (Exception ex) { hotkeyError = ex; }
});
hotkeyThread.SetApartmentState(ApartmentState.STA);
hotkeyThread.Start();
hotkeyThread.Join();
Check("hotkeys without exceptions", hotkeyError is null, hotkeyError?.Message ?? "");

// --- 23b. Auto-detect: slot swap, albedo skip, manual drop warning ---
Exception? slotRoutingError = null;
var slotRoutingThread = new Thread(() =>
{
    try
    {
        var mysteryPath = SolidPng(Path.Combine(dir, "mystery.png"), 2, 2, 111, 111, 111);
        var diffusePath = SolidPng(Path.Combine(dir, "T_Rock_Diffuse.png"), 2, 2, 60, 60, 60);
        var roughPath = Path.Combine(dir, "T_Rock_Roughness.png");
        var addFiles = typeof(MainForm).GetMethod("AddFiles", BindingFlags.NonPublic | BindingFlags.Instance)!;

        using var form = NewForm();
        var slots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        // An unrecognized file occupies the Roughness slot; the recognized map should displace it.
        slots.GetValue(1)!.GetType().GetField("Image", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(slots.GetValue(1), TextureImage.Load(mysteryPath));
        slots.GetValue(1)!.GetType().GetField("Path", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(slots.GetValue(1), mysteryPath);
        addFiles.Invoke(form, new object?[] { new[] { roughPath }, null });
        string? SlotPath(int index) => (string?)slots.GetValue(index)!.GetType()
            .GetField("Path", BindingFlags.Public | BindingFlags.Instance)!.GetValue(slots.GetValue(index));
        Check("auto-detect: recognized map takes its slot, occupant is moved",
            (SlotPath(1) ?? "").EndsWith("T_Rock_Roughness.png", StringComparison.Ordinal) &&
            (SlotPath(0) ?? "").EndsWith("mystery.png", StringComparison.Ordinal),
            $"R={SlotPath(0)}, G={SlotPath(1)}");

        var files = (System.Collections.ICollection)typeof(MainForm)
            .GetField("_files", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var before = files.Count;
        addFiles.Invoke(form, new object?[] { new[] { diffusePath }, null });
        var diffuseSlot = Enumerable.Range(0, 4).FirstOrDefault(i =>
            (SlotPath(i) ?? "").EndsWith("T_Rock_Diffuse.png", StringComparison.Ordinal), -1);
        string? SlotNotice(Array slotArray, int index) => (string?)slotArray.GetValue(index)!.GetType()
            .GetProperty("MismatchNotice", BindingFlags.Public | BindingFlags.Instance)!.GetValue(slotArray.GetValue(index));
        Check("GUI: albedo/diffuse is added to a free slot with a soft warning",
            files.Count == before + 1 && diffuseSlot >= 0 &&
            (SlotNotice(slots, diffuseSlot) ?? "").Contains("Albedo"),
            $"slot={diffuseSlot}, notice={SlotNotice(slots, diffuseSlot)}");

        // Edge case: AO is named _diff but must still be added and used.
        var misnamedPath = SolidPng(Path.Combine(dir, "rusty_metal_04_diff_1k.jpg"), 2, 2, 60, 60, 60);
        using var misnamedForm = NewForm();
        var misnamedSlots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(misnamedForm)!;
        addFiles.Invoke(misnamedForm, new object?[] { new[] { misnamedPath }, null });
        Check("misnamed AO (_diff) is added to a slot with a hint",
            (SlotNotice(misnamedSlots, 0) ?? "").Contains("Albedo") &&
            ((Label)misnamedSlots.GetValue(0)!.GetType()
                .GetProperty("Info", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(misnamedSlots.GetValue(0))!).Text.Contains("rusty_metal_04_diff_1k"),
            SlotNotice(misnamedSlots, 0) ?? "");

        using var manualForm = NewForm();
        var manualSlots = (Array)typeof(MainForm).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(manualForm)!;
        string? ManualNotice(int index) => (string?)manualSlots.GetValue(index)!.GetType()
            .GetProperty("MismatchNotice", BindingFlags.Public | BindingFlags.Instance)!.GetValue(manualSlots.GetValue(index));
        addFiles.Invoke(manualForm, new object?[] { new[] { roughPath }, manualSlots.GetValue(0) });
        Check("manual drop into a different slot: soft warning under the slot",
            ManualNotice(0)!.Contains("Roughness") &&
            ManualNotice(0)!.Contains(Localization.Get("slot.warn.manualMismatch").Split('{')[0].Trim()),
            ManualNotice(0) ?? "");
        Localization.SetLanguage("ru");
        Check("manual-drop warning follows language changes",
            (ManualNotice(0) ?? "").Contains("похоже на"), ManualNotice(0) ?? "");
        Localization.SetLanguage("en");

        addFiles.Invoke(manualForm, new object?[] { new[] { roughPath }, manualSlots.GetValue(1) });
        Check("manual drop into its own slot: warning clears", ManualNotice(1)?.Length == 0);
    }
    catch (Exception ex) { slotRoutingError = ex; }
});
slotRoutingThread.SetApartmentState(ApartmentState.STA);
slotRoutingThread.Start();
slotRoutingThread.Join();
Check("slot routing without exceptions", slotRoutingError is null, slotRoutingError?.Message ?? "");

// --- 24. CLI pack / convert / batch ---
var cliOut = new StringWriter();
var cliPacked = Path.Combine(dir, "cli_packed.png");
var cliCode = Cli.Run(["pack", "--preset", "orm", "--ao", Path.Combine(dir, "T_Rock_AO.png"),
    "--rough", Path.Combine(dir, "T_Rock_Roughness.png"), "--metal", Path.Combine(dir, "T_Rock_Metallic.png"),
    "--out", cliPacked], cliOut);
var cliImage = TextureImage.Load(cliPacked);
var cliResult = Packer.Pack(2, 2,
    new ChannelInput(cliImage, SourceComponent.Red, 255, false),
    new ChannelInput(cliImage, SourceComponent.Green, 255, false),
    new ChannelInput(cliImage, SourceComponent.Blue, 255, false), solid, false);
Check("CLI pack", cliCode == 0 && cliResult.Pixels[2] == 10 && cliResult.Pixels[1] == 20 &&
    cliResult.Pixels[0] == 30, cliOut.ToString().Trim());

var cliConvertOut = new StringWriter();
var cliConverted = Path.Combine(dir, "cli_converted.png");
var cliConvertCode = Cli.Run(["convert", "--from", "hdrp", "--to", "orm",
    "--in", Path.Combine(dir, "swizzle_hdrp.png"), "--out", cliConverted], cliConvertOut);
var cliConvertedImage = TextureImage.Load(cliConverted);
var cliConvertedResult = Packer.Pack(2, 2,
    new ChannelInput(cliConvertedImage, SourceComponent.Red, 255, false),
    new ChannelInput(cliConvertedImage, SourceComponent.Green, 255, false),
    new ChannelInput(cliConvertedImage, SourceComponent.Blue, 255, false), solid, false);
Check("CLI convert", cliConvertCode == 0 && cliConvertedResult.Pixels[2] == 10 &&
    cliConvertedResult.Pixels[1] == 215 && cliConvertedResult.Pixels[0] == 30);

string setsDir = Path.Combine(dir, "sets"), batchOut = Path.Combine(dir, "batched");
if (Directory.Exists(batchOut)) Directory.Delete(batchOut, true);
Directory.CreateDirectory(Path.Combine(setsDir, "set_a"));
Directory.CreateDirectory(Path.Combine(setsDir, "set_b"));
SolidPng(Path.Combine(setsDir, "set_a", "T_Set_AO.png"), 2, 2, 10, 10, 10);
SolidPng(Path.Combine(setsDir, "set_a", "T_Set_Roughness.png"), 2, 2, 20, 20, 20);
SolidPng(Path.Combine(setsDir, "set_a", "T_Set_Metallic.png"), 2, 2, 30, 30, 30);
SolidPng(Path.Combine(setsDir, "set_b", "T_Other_Normal.png"), 2, 2, 128, 128, 255);
var batchOutWriter = new StringWriter();
var batchCode = Cli.Run(["batch", "--in", setsDir, "--out", batchOut, "--preset", "orm"], batchOutWriter);
var batchText = batchOutWriter.ToString();
Check("CLI batch: one set packed, empty set skipped",
    batchCode == 0 && File.Exists(Path.Combine(batchOut, "T_Set_ORM.png")) &&
    batchText.Contains("1 packed, 1 skipped") && batchText.Contains("skipped: set_b"), batchText.Trim());

// --- 24b. Channel unpacking: core, CLI and UI ---
var unpackDir = Path.Combine(dir, "unpacked");
var unpacked = PackerConvert.UnpackChannels(hdrpSource, unpackDir, "T_Unpack");
Check("core unpack: 4 grayscale PNG",
    unpacked.Count == 4 && unpacked.All(File.Exists) &&
    unpacked.Select(p => Path.GetFileName(p)).SequenceEqual(
        ["T_Unpack_R.png", "T_Unpack_G.png", "T_Unpack_B.png", "T_Unpack_A.png"]));
Check("core unpack: channel values preserved",
    new[] { 30, 10, 200, 40 }.Select((expected, i) =>
    {
        using var image = new Bitmap(unpacked[i]);
        return image.GetPixel(0, 0).R == expected;
    }).All(ok => ok));

var cliUnpackDir = Path.Combine(dir, "unpacked-cli");
var cliUnpackOut = new StringWriter();
var cliUnpackCode = Cli.Run(["unpack", "--in", Path.Combine(dir, "swizzle_hdrp.png"),
    "--out", cliUnpackDir, "--name", "T_Cli"], cliUnpackOut);
Check("CLI unpack",
    cliUnpackCode == 0 && new[] { "R", "G", "B", "A" }
        .All(ch => File.Exists(Path.Combine(cliUnpackDir, "T_Cli_" + ch + ".png"))),
    cliUnpackOut.ToString().Trim());

Exception? unpackUiError = null;
var unpackThread = new Thread(() =>
{
    try
    {
        using var form = NewForm();
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        ((ModernComboBox)Field("_modeBox")).SelectedIndex = 1;
        typeof(MainForm).GetMethod("AddFiles", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(form, new object?[] { new[] { Path.Combine(dir, "swizzle_hdrp.png") }, null });

        ((ModernComboBox)Field("_sizeBox")).SelectedItem = OutputSize.Original;
        var targetBox = (ModernComboBox)Field("_convertToPresetBox");
        targetBox.SelectedItem = Preset.UnityUrp;
        var nameBox = (Control)Field("_nameBox");
        var mapping = (Array)typeof(MainForm).GetField("_mappingLabels", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(form)!;
        Check("convert: target change rebuilds name, mapping and preview at once",
            ReferenceEquals(((ModernComboBox)Field("_presetBox")).SelectedItem, Preset.UnityUrp) &&
            nameBox.Text.EndsWith("_MetallicSmoothness.png", StringComparison.Ordinal) &&
            ((Label)mapping.GetValue(0)!).Text.Contains("Metallic") &&
            ((Control)Field("_previewInfo")).Text.Contains("RGB"),
            nameBox.Text + " | " + ((Label)mapping.GetValue(0)!).Text);

        foreach (var channel in new[] { "R", "G", "B", "A" })
        {
            var stale = Path.Combine(dir, "swizzle_hdrp_" + channel + ".png");
            if (File.Exists(stale)) File.Delete(stale);
        }
        var unpackButton = (ModernButton)Field("_unpackButton");
        typeof(ModernButton).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(unpackButton, new object[] { EventArgs.Empty });
        Check("convert UI: unpack writes four channels and reports them",
            ((Control)Field("_statusLabel")).Text.Contains(
                string.Format(Localization.Get("convert.unpacked"), "4")) &&
            new[] { "R", "G", "B", "A" }.All(ch =>
                File.Exists(Path.Combine(dir, "swizzle_hdrp_" + ch + ".png"))),
            ((Control)Field("_statusLabel")).Text);
        var unpackedR = TextureImage.Load(Path.Combine(dir, "swizzle_hdrp_R.png"));
        Check("convert UI unpacks the result, not the source",
            unpackedR.Width == 2 && unpackedR.Height == 2 && unpackedR.Pixels[2] == 30 &&
            unpackedR.Pixels[0] != 200,
            $"{unpackedR.Width}x{unpackedR.Height} R={unpackedR.Pixels[2]}");
    }
    catch (Exception ex) { unpackUiError = ex; }
});
unpackThread.SetApartmentState(ApartmentState.STA);
unpackThread.Start();
unpackThread.Join();
Check("unpack UI without exceptions", unpackUiError is null, unpackUiError?.Message ?? "");

// --- 24c. Pack-mode UI unpack uses the packed result at full resolution ---
Exception? packUnpackError = null;
var packUnpackThread = new Thread(() =>
{
    try
    {
        var first = SolidPng(Path.Combine(dir, "resultdata_ao.png"), 2, 2, 10, 10, 10);
        var second = SolidPng(Path.Combine(dir, "resultdata_rough.png"), 2, 2, 20, 20, 20);
        var third = SolidPng(Path.Combine(dir, "resultdata_metal.png"), 2, 2, 30, 30, 30);
        using var form = NewForm([first, second, third]);
        Control Field(string name) => (Control)typeof(MainForm)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        ((ModernComboBox)Field("_sizeBox")).SelectedItem = OutputSize.Original;
        var stem = Path.GetFileNameWithoutExtension(OutputNaming.Suggest(new[] { first }, "", ".png"));
        typeof(ModernButton).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(Field("_unpackButton"), new object[] { EventArgs.Empty });

        static byte Red(string path) => TextureImage.Load(path).Pixels[2];
        var paths = new[] { "R", "G", "B", "A" }
            .Select(ch => Path.Combine(dir, stem + "_" + ch + ".png")).ToArray();
        Check("pack UI unpack writes full-res result channels",
            paths.All(File.Exists) && TextureImage.Load(paths[0]).Width == 2 &&
            Red(paths[0]) == 10 && Red(paths[1]) == 20 &&
            Red(paths[2]) == 30 && Red(paths[3]) == 255,
            $"stem={stem}, " + string.Join(",", paths.Select(p => File.Exists(p) ? Red(p).ToString() : "missing")));
    }
    catch (Exception ex) { packUnpackError = ex; }
});
packUnpackThread.SetApartmentState(ApartmentState.STA);
packUnpackThread.Start();
packUnpackThread.Join();
Check("pack unpack UI without exceptions", packUnpackError is null, packUnpackError?.Message ?? "");

// --- 25. Golden files per preset ---
var goldenRoot = AppContext.BaseDirectory;
while (goldenRoot is not null && !Directory.Exists(Path.Combine(goldenRoot, "golden")))
    goldenRoot = Directory.GetParent(goldenRoot)?.FullName;
if (goldenRoot is null) Console.WriteLine("SKIP  golden files not found");
else
{
    foreach ((var id, var preset) in new[]
             {
                 ("orm", Preset.Orm), ("hdrp", Preset.UnityHdrp),
                 ("urp", Preset.UnityUrp), ("custom", Preset.Custom)
             })
    {
        var inputs = new ChannelInput[4];
        foreach (var channel in new[] { Channel.R, Channel.G, Channel.B, Channel.A })
        {
            byte value = preset.Channels[channel].Kind switch
            {
                MapKind.Occlusion => 10,
                MapKind.Roughness => 20,
                MapKind.Metallic => 30,
                MapKind.Smoothness => 50,
                MapKind.DetailMask => 60,
                MapKind.Height => 40,
                _ => (byte)(11 + (int)channel)
            };
            inputs[(int)channel] = new ChannelInput(null, SourceComponent.Red, value, false);
        }
        var alpha = preset.AlphaMode != AlphaMode.None;
        var goldenPack = Packer.Pack(4, 4, inputs[0], inputs[1], inputs[2], inputs[3], alpha);
        var actualPath = Path.Combine(dir, "golden_" + id + ".png");
        Packer.SavePng(goldenPack, actualPath, alpha);
        var expectedPath = Path.Combine(goldenRoot, "golden", id + ".png");
        Check($"golden: {id}",
            File.Exists(expectedPath) && SHA256.HashData(File.ReadAllBytes(expectedPath))
                .SequenceEqual(SHA256.HashData(File.ReadAllBytes(actualPath))));
    }
}

// --- 26. Error paths: CLI validation and corrupt inputs ---
static bool Throws<TException>(Action action) where TException : Exception
{
    try { action(); return false; }
    catch (TException) { return true; }
    catch (Exception) { return false; }
}

Check("CLI usage for unknown command", Cli.Run(["unknown"], new StringWriter()) == 2);
var missingWriter = new StringWriter();
var missingCode = Cli.Run(["pack", "--preset", "orm", "--ao", Path.Combine(dir, "missing.png"),
    "--out", Path.Combine(dir, "never.png")], missingWriter);
Check("CLI pack reports a missing input map", missingCode == 1 &&
    missingWriter.ToString().Contains("not found", StringComparison.OrdinalIgnoreCase), missingWriter.ToString().Trim());
var badSizeWriter = new StringWriter();
var badSizeCode = Cli.Run(["pack", "--preset", "orm", "--ao", Path.Combine(dir, "T_Rock_AO.png"),
    "--size", "not-a-size", "--out", Path.Combine(dir, "never.png")], badSizeWriter);
Check("CLI pack rejects an unknown size", badSizeCode == 1 &&
    badSizeWriter.ToString().Contains("Unknown size", StringComparison.Ordinal), badSizeWriter.ToString().Trim());
var badFormatWriter = new StringWriter();
var badFormatCode = Cli.Run(["convert", "--from", "orm", "--to", "hdrp",
    "--in", Path.Combine(dir, "swizzle_orm.png"), "--format", "webp",
    "--out", Path.Combine(dir, "never.webp")], badFormatWriter);
Check("CLI rejects an unknown format", badFormatCode == 1 &&
    badFormatWriter.ToString().Contains("Unknown format", StringComparison.Ordinal), badFormatWriter.ToString().Trim());
var shortTga = Path.Combine(dir, "corrupt.tga");
File.WriteAllBytes(shortTga, [1, 2, 3]);
Check("TGA decoder rejects a truncated file", Throws<InvalidDataException>(() => TextureImage.Load(shortTga)));
var badTypeTga = Path.Combine(dir, "bad_type.tga");
File.WriteAllBytes(badTypeTga, [0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 2, 0, 2, 0, 24, 0, 24, 0]);
Check("TGA decoder rejects an unsupported type", Throws<NotSupportedException>(() => TextureImage.Load(badTypeTga)));
Check("FromPixels rejects a short buffer", Throws<ArgumentException>(
    () => TextureImage.FromPixels("bad.png", 2, 2, new byte[15])));
Check("FromPixels rejects invalid dimensions", Throws<ArgumentOutOfRangeException>(
    () => TextureImage.FromPixels("bad.png", 0, 2, new byte[16])));

var oddSession = Path.Combine(dir, "odd-session.json");
File.WriteAllText(oddSession,
    "{\"Slots\":[null,{\"Path\":null,\"Source\":99,\"SolidValue\":7,\"Invert\":false}],\"RecentPaths\":null}");
var oddLoaded = SessionStore.Load(oddSession);
Check("session tolerates null and invalid entries",
    oddLoaded.Slots.Length == 2 && oddLoaded.Slots[0] is not null &&
    Enum.IsDefined(oddLoaded.Slots[1].Source) && oddLoaded.RecentPaths is not null);

var nullSlotsSession = Path.Combine(dir, "null-slots-session.json");
File.WriteAllText(nullSlotsSession, "{\"Slots\":null}");
Check("session tolerates null slot array", SessionStore.Load(nullSlotsSession).Slots.Length == 0);
var helpWriter = new StringWriter();
Check("CLI help prints usage without an error", Cli.IsHelpRequest(["--help"]) &&
    Cli.Run(["--help"], helpWriter) == 0 && helpWriter.ToString().Contains("pack", StringComparison.Ordinal));
Check("CLI version flag is recognized", Cli.IsVersionRequest(["--version"]) &&
    Cli.IsVersionRequest(["-v"]) && !Cli.IsVersionRequest(["pack"]) &&
    System.Text.RegularExpressions.Regex.IsMatch(Cli.VersionText(), @"\d+\.\d+\.\d+"), Cli.VersionText());

Console.WriteLine(failures == 0 ? "\nAll tests passed." : $"\nFailed tests: {failures}");
Environment.ExitCode = failures == 0 ? 0 : 1;


