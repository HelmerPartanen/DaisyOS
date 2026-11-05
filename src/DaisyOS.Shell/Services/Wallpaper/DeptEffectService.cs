using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>Offline segmentation of a small snapshot of the presented wallpaper. No display resources are retained.</summary>
public sealed class DeptEffectService
{
    public const int MaskSize = 320;
    public const int OverlaySize = 640;
    private static readonly SemaphoreSlim InferenceGate = new(1);
    private static readonly string ModelDirectory = Path.Combine(AppContext.BaseDirectory, "Models", "DeptEffect");
    private static readonly Lazy<string> ModelVersion = new(() => string.Join("-", new[] { "skyseg.onnx", "u2netp.onnx" }
        .Select(HashModel)));
    private static readonly object MemoryCacheGate = new();
    private static readonly Dictionary<string, byte[][]> MemoryCache = new(StringComparer.Ordinal);
    private static readonly object SessionGate = new();
    private static InferenceSession? SkySession;
    private static InferenceSession? SubjectSession;
    private readonly string _cacheDirectory;

    private static string HashModel(string name)
    {
        using var stream = File.OpenRead(Path.Combine(ModelDirectory, name));
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static InferenceSession GetSession(string model, bool sky)
    {
        lock (SessionGate)
        {
            ref var session = ref (sky ? ref SkySession : ref SubjectSession);
            if (session is not null) return session;
            var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
            session = new InferenceSession(Path.Combine(ModelDirectory, model), options);
            return session;
        }
    }

    public DeptEffectService(string? cacheDirectory = null)
    {
        var root = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        _cacheDirectory = cacheDirectory ?? Path.Combine(string.IsNullOrEmpty(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache") : root, "DaisyOS", "dept-effect");
    }

    // A cache entry contains both masks, including negative results. Strength and clock text never invalidate inference.
    public async Task<byte[][]?> AnalyzeAsync(byte[] snapshot, bool automatic, CancellationToken cancellationToken)
    {
        try
        {
            if (snapshot is not { Length: > 0 }) return null;
            return await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = Convert.ToHexString(SHA256.HashData(snapshot)) + "-v3-" + ModelVersion.Value;
                key = Convert.ToHexString(SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(key)));
                var path = Path.Combine(_cacheDirectory, key + ".mask");
                var memoryKey = path;
                lock (MemoryCacheGate)
                {
                    if (MemoryCache.TryGetValue(memoryKey, out var inMemory) && File.Exists(path))
                        return inMemory;
                }
                await InferenceGate.WaitAsync(cancellationToken);
                try
                {
                    lock (MemoryCacheGate)
                    {
                        if (MemoryCache.TryGetValue(memoryKey, out var inMemory) && File.Exists(path))
                            return inMemory;
                    }
                    if (File.Exists(path))
                    {
                        var cached = await File.ReadAllBytesAsync(path, cancellationToken);
                        if (cached.Length == OverlaySize * OverlaySize * 2)
                        {
                            var loaded = new[] { cached[..(OverlaySize * OverlaySize)], cached[(OverlaySize * OverlaySize)..] };
                            lock (MemoryCacheGate)
                            {
                                if (MemoryCache.Count >= 16) MemoryCache.Clear();
                                MemoryCache[memoryKey] = loaded;
                            }
                            return loaded;
                        }
                    }
                    if (!automatic) return null;
                    using var image = SKBitmap.Decode(snapshot);
                    if (image is null) return null;
                    using var input = image.Resize(new SKImageInfo(MaskSize, MaskSize), new SKSamplingOptions(SKFilterMode.Linear));
                    if (input is null) return null;
                    var masks = new[] {
                        RefineMask(Infer(input, "skyseg.onnx", true, cancellationToken), input, image, cancellationToken),
                        RefineMask(Infer(input, "u2netp.onnx", false, cancellationToken), input, image, cancellationToken) };
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(_cacheDirectory);
                    var temporary = path + "." + Guid.NewGuid().ToString("N");
                    try
                    {
                        var combined = new byte[OverlaySize * OverlaySize * 2];
                        Buffer.BlockCopy(masks[0], 0, combined, 0, OverlaySize * OverlaySize);
                        Buffer.BlockCopy(masks[1], 0, combined, OverlaySize * OverlaySize, OverlaySize * OverlaySize);
                        await File.WriteAllBytesAsync(temporary, combined, cancellationToken);
                        File.Move(temporary, path, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    lock (MemoryCacheGate)
                    {
                        if (MemoryCache.Count >= 16) MemoryCache.Clear();
                        MemoryCache[memoryKey] = masks;
                    }
                    // Bound the disk cache (each entry is only 800 KiB).
                    foreach (var old in new DirectoryInfo(_cacheDirectory).GetFiles("*.mask").OrderByDescending(f => f.LastWriteTimeUtc).Skip(64))
                        old.Delete();
                    return masks;
                }
                finally { InferenceGate.Release(); }
            }, cancellationToken);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DeptEffect] Using ordinary wallpaper: {ex.Message}");
            return null;
        }
    }

    private static byte[] Infer(SKBitmap image, string model, bool sky, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var session = GetSession(model, sky);
        using var runOptions = new RunOptions();
        using var cancellation = token.Register(() => runOptions.Terminate = true);
        var tensor = new DenseTensor<float>(new[] { 1, 3, MaskSize, MaskSize });
        float[] means = [0.485f, 0.456f, 0.406f], deviations = [0.229f, 0.224f, 0.225f];
        var maximum = 1f;
        if (!sky)
            for (var y = 0; y < MaskSize; y++)
                for (var x = 0; x < MaskSize; x++)
                {
                    var p = image.GetPixel(x, y);
                    maximum = Math.Max(maximum, Math.Max(p.Red, Math.Max(p.Green, p.Blue)));
                }
        var normScale = 1f / (sky ? 255f : maximum);
        var invDev0 = 1f / deviations[0];
        var invDev1 = 1f / deviations[1];
        var invDev2 = 1f / deviations[2];
        for (var y = 0; y < MaskSize; y++)
            for (var x = 0; x < MaskSize; x++)
            {
                var p = image.GetPixel(x, y);
                tensor[0, 0, y, x] = (p.Red * normScale - means[0]) * invDev0;
                tensor[0, 1, y, x] = (p.Green * normScale - means[1]) * invDev1;
                tensor[0, 2, y, x] = (p.Blue * normScale - means[2]) * invDev2;
            }
        using var output = session.Run(new[] { NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), tensor) }, session.OutputMetadata.Keys.Take(1).ToArray(), runOptions);
        var prediction = output.First().AsTensor<float>().ToArray();
        if (prediction.Length != MaskSize * MaskSize || prediction.Any(v => !float.IsFinite(v))) return new byte[MaskSize * MaskSize];
        var min = prediction.Min();
        var range = prediction.Max() - min;
        if (range < 0.05) return new byte[prediction.Length];
        var mask = new byte[prediction.Length];
        var uncertain = 0;
        var invRange = 1f / range;
        for (var i = 0; i < mask.Length; i++)
        {
            var value = (prediction[i] - min) * invRange;
            // Sky model emits high values for sky; invert to retain mountains, trees and buildings.
            if (sky) value = 1 - value;
            if (value > 0.1 && value < 0.9) uncertain++;
            // A narrow smoothstep removes low-confidence halos without widening subjects.
            var alpha = Math.Clamp((value - 0.35f) * 2.5f, 0, 1);
            mask[i] = (byte)Math.Round(alpha * alpha * (3 - 2 * alpha) * 255);
        }
        if (!IsCompatible(mask, uncertain / (double)mask.Length, sky) || !HasUsableEdges(mask, image)) Array.Clear(mask);
        return mask;
    }

    private static bool HasUsableEdges(byte[] mask, SKBitmap image)
    {
        double contrast = 0;
        var edges = 0;
        for (var y = 2; y < MaskSize - 2; y++)
            for (var x = 2; x < MaskSize - 2; x++)
            {
                if ((mask[y * MaskSize + x] > 127) == (mask[(y + 1) * MaskSize + x] > 127) &&
                    (mask[y * MaskSize + x] > 127) == (mask[y * MaskSize + x + 1] > 127)) continue;
                var a = image.GetPixel(x - 2, y - 2);
                var b = image.GetPixel(x + 2, y + 2);
                contrast += Math.Sqrt(Math.Pow(a.Red - b.Red, 2) + Math.Pow(a.Green - b.Green, 2) + Math.Pow(a.Blue - b.Blue, 2));
                edges++;
            }
        return edges >= 20 && contrast / edges >= 12;
    }

    // Joint bilateral upsampling follows the original image's colour edges, rather than blurring
    // a 320px silhouette across the sky. Refinement happens once, before writing the disk cache.
    private static byte[] RefineMask(byte[] mask, SKBitmap input, SKBitmap guide, CancellationToken token)
    {
        var result = new byte[OverlaySize * OverlaySize];
        for (var y = 0; y < OverlaySize; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < OverlaySize; x++)
            {
                var sx = (x + 0.5) * MaskSize / OverlaySize - 0.5;
                var sy = (y + 0.5) * MaskSize / OverlaySize - 0.5;
                var centerX = Math.Clamp((int)Math.Round(sx), 0, MaskSize - 1);
                var centerY = Math.Clamp((int)Math.Round(sy), 0, MaskSize - 1);
                var center = mask[centerY * MaskSize + centerX];
                if ((center == 0 || center == 255) &&
                    mask[Math.Max(0, centerY - 1) * MaskSize + centerX] == center &&
                    mask[Math.Min(MaskSize - 1, centerY + 1) * MaskSize + centerX] == center &&
                    mask[centerY * MaskSize + Math.Max(0, centerX - 1)] == center &&
                    mask[centerY * MaskSize + Math.Min(MaskSize - 1, centerX + 1)] == center)
                {
                    result[y * OverlaySize + x] = center;
                    continue;
                }
                var pixel = guide.GetPixel(x * guide.Width / OverlaySize, y * guide.Height / OverlaySize);
                double sum = 0, weights = 0;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var ix = Math.Clamp(centerX + dx, 0, MaskSize - 1);
                        var iy = Math.Clamp(centerY + dy, 0, MaskSize - 1);
                        var color = input.GetPixel(ix, iy);
                        var distance = Math.Pow(color.Red - pixel.Red, 2) + Math.Pow(color.Green - pixel.Green, 2) + Math.Pow(color.Blue - pixel.Blue, 2);
                        var weight = Math.Exp(-distance / 1800 - ((ix - sx) * (ix - sx) + (iy - sy) * (iy - sy)) / 2);
                        weights += weight;
                        sum += weight * mask[iy * MaskSize + ix];
                    }
                result[y * OverlaySize + x] = weights > 1e-12 ? (byte)Math.Clamp(Math.Round(sum / weights), 0, 255) : center;
            }
        }
        return result;
    }

    public static bool IsCompatible(byte[] mask, double uncertainFraction, bool skyline)
    {
        if (mask.Length != MaskSize * MaskSize || uncertainFraction > 0.25) return false;
        var coverage = mask.Count(v => v > 127) / (double)mask.Length;
        if (coverage < 0.04 || coverage > 0.92) return false;
        if (skyline)
        {
            var top = mask.Take(MaskSize * 8).Average(v => (double)v) / 255;
            var bottom = mask.TakeLast(MaskSize * 8).Average(v => (double)v) / 255;
            if (top > 0.15 || bottom < 0.6) return false;
        }
        return true;
    }
}
