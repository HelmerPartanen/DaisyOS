using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using DaisyOS.Shell.Services.Theming;

if (args.Any(argument => argument != "--all-fonts"))
    throw new ArgumentException("Usage: TypographyPerformance [--all-fonts]");
LinuxTextLibraryIsolation.EnsureLoaded();
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions())
    .UseSkia().UseHarfBuzz().With(new FontManagerOptions { DefaultFamilyName = TypographyPolicy.BundledDefaultFamilyName }).SetupWithoutStarting();
var fixture = "ÅÉgjpq 日本語 العربية हिन्दी";
var roles = new (string Role, double Size, double Line)[] {
    ("Caption",12,16),("BodySmall",13,18),("Body",14,20),("BodyLarge",16,24),
    ("Title",20,28),("Heading",24,32),("Display",32,40),("DisplayLarge",44,52),("DisplayHero",104,112) };
var samples = new List<object>();
for (int run = -2; run < 30; run++)
{
    var resources = new ResourceDictionary();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var watch = Stopwatch.StartNew();
    TypographyPolicy.Apply(resources, "Inter", 100);
    watch.Stop();
    var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    if (run >= 0) samples.Add(new { elapsedMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = bytes });
}
// Verify the production resources against the established size-by-size
// algorithm, including all supported integer scale percentages.
int comparisons = 0;
var mismatches = new List<object>();
var families = new[] {
    "Inter", "Atkinson Hyperlegible", "Noto Sans", "DejaVu Sans", "DejaVu Serif", "Liberation Sans",
    "Noto Fangsong KSS Rotated", "Noto Fangsong KSS Vertical", "Noto Serif Khitan Small Script"
}.AsEnumerable();
if (args.Contains("--all-fonts")) families = families.Concat(FontManager.Current.SystemFonts.Select(font => font.Name));
var checkedFamilies = families.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
var checkedCount = 0;
foreach (var family in checkedFamilies)
{
    var face = new Typeface(TypographyPolicy.ResolveFontFamily(family));
    foreach (int percent in Enumerable.Range(90, 61))
    {
        var resources = new ResourceDictionary();
        TypographyPolicy.Apply(resources, family, percent);
        foreach (var (role, baseSize, baseLine) in roles)
        {
            var scale = percent / 100d;
            var size = Math.Round(baseSize * scale, MidpointRounding.AwayFromZero);
            using var legacy = new TextLayout(fixture, face, size);
            var expected = Math.Ceiling(Math.Max(baseLine * scale, legacy.Height));
            var actual = (double)resources["TypeLine" + role]!;
            comparisons++;
            if (expected != actual) mismatches.Add(new { family, percent, role, expected, actual });
        }
    }
    checkedCount++;
    if (args.Contains("--all-fonts") && checkedCount % 100 == 0)
        Console.Error.WriteLine($"Parity fonts: {checkedCount}/{checkedFamilies.Length}; mismatches: {mismatches.Count}");
}
var assembly = typeof(TypographyPolicy).Assembly;
var report = new
{
    protocol = 1,
    configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
    shellAssemblySha256 = Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly.Location))),
    samples, checkedFamilies, comparisons, mismatches
};
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return mismatches.Count == 0 ? 0 : 1;
