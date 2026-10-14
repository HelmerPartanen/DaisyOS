using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using SkiaSharp;

namespace DaisyOS.Shell.Services.Media;

/// <summary>
/// Lightweight, self-contained color extractor that downscales artwork to 32x32,
/// extracts a dominant palette via median-cut quantization, selects a representative
/// chromatic accent (with neutral fallback), and normalizes extremes for widget tinting.
/// </summary>
public static class ArtworkColorExtractor
{
    private const int TargetDimension = 32;
    private const int MaxClusters = 8;
    private const int MaxCacheEntries = 128;

    /// <summary>
    /// Default tint opacity overlay applied to the media widget background (35% opacity).
    /// </summary>
    public const float DefaultTintOpacity = 0.35f;

    /// <summary>
    /// Alpha byte value (0-255) derived from DefaultTintOpacity (~89 for 35%).
    /// </summary>
    public static byte DefaultTintAlpha => (byte)Math.Clamp(MathF.Round(DefaultTintOpacity * 255f), 0, 255);

    private static readonly ConcurrentDictionary<string, Color> _cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> _cacheOrder = new();

    /// <summary>
    /// Returns the cached accent color for the specified key, or extracts, normalizes,
    /// and caches it from the provided artwork bytes.
    /// </summary>
    public static Color? GetOrExtract(string? artworkKey, byte[]? artworkBytes)
    {
        if (!string.IsNullOrWhiteSpace(artworkKey) && _cache.TryGetValue(artworkKey, out var cached))
        {
            return cached;
        }

        if (artworkBytes is null || artworkBytes.Length == 0)
        {
            return null;
        }

        var extracted = ExtractAccentColor(artworkBytes);
        if (extracted is { } color && !string.IsNullOrWhiteSpace(artworkKey))
        {
            _cache[artworkKey] = color;
            _cacheOrder.Enqueue(artworkKey);

            while (_cache.Count > MaxCacheEntries && _cacheOrder.TryDequeue(out var staleKey))
            {
                _cache.TryRemove(staleKey, out _);
            }
        }

        return extracted;
    }

    /// <summary>
    /// Extracts a representative normalized accent color from artwork bytes.
    /// </summary>
    public static Color? ExtractAccentColor(byte[] artworkBytes)
    {
        try
        {
            using var stream = new MemoryStream(artworkBytes, writable: false);
            using var original = SKBitmap.Decode(stream);
            if (original is null || original.Width <= 0 || original.Height <= 0)
            {
                return null;
            }

            var info = new SKImageInfo(TargetDimension, TargetDimension, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var resized = original.Resize(info, SKSamplingOptions.Default);
            if (resized is null)
            {
                return null;
            }

            var pixels = new List<PixelRgb>(TargetDimension * TargetDimension);
            var span = resized.GetPixelSpan();
            for (var i = 0; i < span.Length; i += 4)
            {
                byte r = span[i];
                byte g = span[i + 1];
                byte b = span[i + 2];
                byte a = span[i + 3];

                if (a >= 128)
                {
                    pixels.Add(new PixelRgb(r, g, b));
                }
            }

            if (pixels.Count == 0)
            {
                return null;
            }

            // Extract 6-8 dominant color clusters using Median-Cut quantization
            var clusters = QuantizeMedianCut(pixels, MaxClusters);
            if (clusters.Count == 0)
            {
                return null;
            }

            // Select and normalize best representative accent
            return SelectAndNormalizeAccent(clusters, pixels.Count);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Composites an artwork accent color at tintAmount (default 35%) over a base container color.
    /// finalColor = (artworkAccent * tintAmount) + (baseContainerColor * (1 - tintAmount))
    /// </summary>
    public static Color ApplyArtworkTint(Color baseColor, Color accent, float tintAmount = DefaultTintOpacity)
    {
        tintAmount = Math.Clamp(tintAmount, 0f, 1f);
        float baseAmount = 1f - tintAmount;

        return Color.FromArgb(
            255,
            (byte)Math.Clamp(MathF.Round((accent.R * tintAmount) + (baseColor.R * baseAmount)), 0, 255),
            (byte)Math.Clamp(MathF.Round((accent.G * tintAmount) + (baseColor.G * baseAmount)), 0, 255),
            (byte)Math.Clamp(MathF.Round((accent.B * tintAmount) + (baseColor.B * baseAmount)), 0, 255)
        );
    }

    private static Color SelectAndNormalizeAccent(List<ColorCluster> clusters, int totalPixels)
    {
        // Analyze candidates in HSL space
        var candidates = clusters.Select(c =>
        {
            var (h, s, l, chroma) = RgbToHsl(c.R, c.G, c.B);
            double popRatio = (double)c.Count / totalPixels;
            return (Cluster: c, H: h, S: s, L: l, Chroma: chroma, PopRatio: popRatio);
        }).ToList();

        // Check if the whole palette is effectively neutral/monochrome
        bool allNeutral = candidates.All(c => c.Chroma < 0.08);

        if (allNeutral)
        {
            // For neutral/grayscale artwork, pick the most dominant candidate with midtone lightness
            var bestNeutral = candidates
                .OrderByDescending(c => c.L >= 0.30 && c.L <= 0.65 ? c.PopRatio * 2.0 : c.PopRatio)
                .First();

            // Normalize neutral lightness to comfortable range without synthesizing false saturation
            double normL = Math.Clamp(bestNeutral.L, 0.30, 0.60);
            return HslToRgb(bestNeutral.H, 0.0, normL);
        }

        // Score candidates for visual representation:
        // - Prefer chromatic/saturated colors over mud or gray
        // - Penalize extreme black (L < 0.12) and extreme white (L > 0.88)
        // - Favor midtones around 0.4 - 0.6
        // - Weight by population so small noise dots don't overpower
        var bestCandidate = candidates
            .Select(c =>
            {
                double extremePenalty = 1.0;
                if (c.L < 0.12 || c.L > 0.88)
                {
                    extremePenalty = 0.05;
                }
                else if (c.L < 0.20 || c.L > 0.80)
                {
                    extremePenalty = 0.4;
                }

                double midtoneBonus = 1.0 - (0.7 * Math.Abs(c.L - 0.5));
                double score = extremePenalty * Math.Pow(c.PopRatio, 0.4) * (c.Chroma + 0.15) * midtoneBonus;

                return (Candidate: c, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .First()
            .Candidate;

        // Normalization:
        // 1. Prevent extremely bright colors from making the widget glaring: L <= 0.65
        // 2. Prevent extremely dark colors from making surface unreadable: L >= 0.28
        // 3. Reduce excessively saturated/neon colors: S <= 0.70
        double normalizedL = Math.Clamp(bestCandidate.L, 0.28, 0.65);
        double normalizedS = Math.Min(bestCandidate.S, 0.70);

        return HslToRgb(bestCandidate.H, normalizedS, normalizedL);
    }

    private static List<ColorCluster> QuantizeMedianCut(List<PixelRgb> pixels, int maxBoxes)
    {
        var boxes = new List<VBox> { new(pixels) };

        while (boxes.Count < maxBoxes)
        {
            // Find the box with the widest color range
            VBox? widest = null;
            int maxRange = -1;
            int widestIndex = -1;

            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Count < 2) continue;

                int range = box.WidestRange;
                if (range > maxRange)
                {
                    maxRange = range;
                    widest = box;
                    widestIndex = i;
                }
            }

            if (widest is null || maxRange <= 0)
            {
                break;
            }

            var (left, right) = widest.Split();
            boxes[widestIndex] = left;
            boxes.Add(right);
        }

        return boxes.Select(b => b.ToCluster()).Where(c => c.Count > 0).ToList();
    }

    public static (double H, double S, double L, double Chroma) RgbToHsl(byte r, byte g, byte b)
    {
        double rf = r / 255.0;
        double gf = g / 255.0;
        double bf = b / 255.0;

        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double chroma = max - min;
        double l = (max + min) / 2.0;

        double h = 0;
        if (chroma > 1e-5)
        {
            if (Math.Abs(max - rf) < 1e-5)
                h = ((gf - bf) / chroma) % 6;
            else if (Math.Abs(max - gf) < 1e-5)
                h = ((bf - rf) / chroma) + 2;
            else
                h = ((rf - gf) / chroma) + 4;

            h *= 60;
            if (h < 0) h += 360;
        }

        double s = (l <= 0 || l >= 1) ? 0 : chroma / (1.0 - Math.Abs(2 * l - 1));
        return (h, s, l, chroma);
    }

    public static Color HslToRgb(double h, double s, double l)
    {
        double c = (1.0 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2 - 1));
        double m = l - (c / 2.0);

        double r1 = 0, g1 = 0, b1 = 0;
        if (h >= 0 && h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h >= 60 && h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h >= 120 && h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h >= 180 && h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h >= 240 && h < 300) { r1 = x; g1 = 0; b1 = c; }
        else if (h >= 300 && h < 360) { r1 = c; g1 = 0; b1 = x; }

        byte r = (byte)Math.Clamp((r1 + m) * 255.0, 0, 255);
        byte g = (byte)Math.Clamp((g1 + m) * 255.0, 0, 255);
        byte b = (byte)Math.Clamp((b1 + m) * 255.0, 0, 255);

        return Color.FromArgb(255, r, g, b);
    }

    private readonly record struct PixelRgb(byte R, byte G, byte B);

    private readonly record struct ColorCluster(byte R, byte G, byte B, int Count);

    private sealed class VBox
    {
        private readonly List<PixelRgb> _pixels;
        public int Count => _pixels.Count;

        private byte _minR, _maxR, _minG, _maxG, _minB, _maxB;

        public int WidestRange
        {
            get
            {
                int rRange = _maxR - _minR;
                int gRange = _maxG - _minG;
                int bRange = _maxB - _minB;
                return Math.Max(rRange, Math.Max(gRange, bRange));
            }
        }

        public VBox(List<PixelRgb> pixels)
        {
            _pixels = pixels;
            ComputeBounds();
        }

        private void ComputeBounds()
        {
            if (_pixels.Count == 0) return;

            _minR = _maxR = _pixels[0].R;
            _minG = _maxG = _pixels[0].G;
            _minB = _maxB = _pixels[0].B;

            for (var i = 1; i < _pixels.Count; i++)
            {
                var p = _pixels[i];
                if (p.R < _minR) _minR = p.R;
                if (p.R > _maxR) _maxR = p.R;
                if (p.G < _minG) _minG = p.G;
                if (p.G > _maxG) _maxG = p.G;
                if (p.B < _minB) _minB = p.B;
                if (p.B > _maxB) _maxB = p.B;
            }
        }

        public (VBox Left, VBox Right) Split()
        {
            int rRange = _maxR - _minR;
            int gRange = _maxG - _minG;
            int bRange = _maxB - _minB;

            // Sort along the widest dimension
            if (rRange >= gRange && rRange >= bRange)
                _pixels.Sort((a, b) => a.R.CompareTo(b.R));
            else if (gRange >= rRange && gRange >= bRange)
                _pixels.Sort((a, b) => a.G.CompareTo(b.G));
            else
                _pixels.Sort((a, b) => a.B.CompareTo(b.B));

            int mid = _pixels.Count / 2;
            var left = new VBox(_pixels.GetRange(0, mid));
            var right = new VBox(_pixels.GetRange(mid, _pixels.Count - mid));
            return (left, right);
        }

        public ColorCluster ToCluster()
        {
            if (_pixels.Count == 0) return new ColorCluster(0, 0, 0, 0);

            long totalR = 0, totalG = 0, totalB = 0;
            foreach (var p in _pixels)
            {
                totalR += p.R;
                totalG += p.G;
                totalB += p.B;
            }

            return new ColorCluster(
                (byte)(totalR / _pixels.Count),
                (byte)(totalG / _pixels.Count),
                (byte)(totalB / _pixels.Count),
                _pixels.Count);
        }
    }
}

