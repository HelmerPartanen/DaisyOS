using System.Diagnostics;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Audio;

public sealed class LinuxAudioSpectrumService : IAudioSpectrumService
{
    private const int BarCount = 6;
    private const int SampleRate = 16_000;
    private const int SampleCount = 512;
    private const int BytesPerSample = 2;
    private const double SilenceThreshold = 0.004;
    // Equal-loudness compensation keeps a raw monitor signal from pinning the
    // bass bands. Values track the six logarithmic band centers from bass to treble.
    private static readonly double[] BandResponseCompensation = [0.22, 0.42, 0.72, 1.00, 1.20, 1.35];
    private static readonly double[] HannWindow = CreateHannWindow();
    private readonly object _syncRoot = new();
    private readonly double[] _latestSpectrum = new double[BarCount];
    private readonly double[] _analyzedSpectrum = new double[BarCount];
    private readonly double[] _real = new double[SampleCount];
    private readonly double[] _imaginary = new double[SampleCount];
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _captureTask;
    private bool _disposed;

    public void CopySpectrum(Span<double> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.Length < BarCount)
        {
            throw new ArgumentException($"Destination must hold at least {BarCount} values.", nameof(destination));
        }

        EnsureCaptureStarted();

        lock (_syncRoot)
        {
            _latestSpectrum.AsSpan().CopyTo(destination);
        }
    }

    private void EnsureCaptureStarted()
    {
        if (_captureTask is not null)
        {
            return;
        }

        lock (_syncRoot)
        {
            _captureTask ??= Task.Run(() => CaptureLoopAsync(_disposeCts.Token));
        }
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var monitorName = await GetDefaultMonitorNameAsync(cancellationToken);
                if (monitorName is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                await CaptureFromMonitorAsync(monitorName, cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                SetSpectrum(ReadOnlySpan<double>.Empty);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static async Task<string?> GetDefaultMonitorNameAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pactl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("get-default-sink");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var sinkName = output.Trim();
        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(sinkName)
            ? $"{sinkName}.monitor"
            : null;
    }

    private async Task CaptureFromMonitorAsync(string monitorName, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "parec",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in new[]
        {
            "--record",
            $"--device={monitorName}",
            "--format=s16le",
            $"--rate={SampleRate}",
            "--channels=1",
            "--raw",
            "--latency-msec=35"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            SetSpectrum(ReadOnlySpan<double>.Empty);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            return;
        }

        try
        {
            var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[SampleCount * BytesPerSample];

            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var read = await ReadExactlyOrPartialAsync(stream, buffer, cancellationToken);
                if (read < buffer.Length)
                {
                    break;
                }

                if (AnalyzeSamples(buffer))
                {
                    SetSpectrum(_analyzedSpectrum);
                }
                else
                {
                    SetSpectrum(ReadOnlySpan<double>.Empty);
                }
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static async Task<int> ReadExactlyOrPartialAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset;
    }

    private bool AnalyzeSamples(byte[] buffer)
    {
        var rms = 0d;
        for (var i = 0; i < SampleCount; i++)
        {
            var sample = BitConverter.ToInt16(buffer, i * BytesPerSample) / 32768d;
            _real[i] = sample * HannWindow[i];
            rms += sample * sample;
        }

        rms = Math.Sqrt(rms / SampleCount);
        if (rms < SilenceThreshold)
        {
            return false;
        }

        Array.Clear(_imaginary);
        FastFourierTransform(_real, _imaginary);
        const double magnitudeNormalization = SampleCount * 0.5;
        var magnitudeNormalizationSquared = magnitudeNormalization * magnitudeNormalization;
        for (var band = 0; band < BarCount; band++)
        {
            var startFrequency = FrequencyForBandEdge(band);
            var endFrequency = FrequencyForBandEdge(band + 1);
            var startBin = Math.Max(1, (int)Math.Floor(startFrequency * SampleCount / SampleRate));
            var endBin = Math.Min((SampleCount / 2) - 1, (int)Math.Ceiling(endFrequency * SampleCount / SampleRate));

            var energy = 0d;
            var count = 0;
            for (var bin = startBin; bin <= endBin; bin++)
            {
                energy += ((_real[bin] * _real[bin]) + (_imaginary[bin] * _imaginary[bin])) / magnitudeNormalizationSquared;
                count++;
            }

            var bandMagnitude = count == 0 ? 0 : Math.Sqrt(energy / count);
            bandMagnitude *= BandResponseCompensation[band];
            var decibels = 20 * Math.Log10(bandMagnitude + 0.000001);
            _analyzedSpectrum[band] = Math.Clamp((decibels + 52) / 46, 0, 1);
        }

        return true;
    }

    private static double[] CreateHannWindow()
    {
        var window = new double[SampleCount];
        for (var i = 0; i < SampleCount; i++)
        {
            window[i] = 0.5 - (0.5 * Math.Cos((2 * Math.PI * i) / (SampleCount - 1)));
        }

        return window;
    }

    private static void FastFourierTransform(double[] real, double[] imaginary)
    {
        for (int i = 1, reversed = 0; i < SampleCount; i++)
        {
            var bit = SampleCount >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
            {
                reversed ^= bit;
            }

            reversed ^= bit;
            if (i < reversed)
            {
                (real[i], real[reversed]) = (real[reversed], real[i]);
                (imaginary[i], imaginary[reversed]) = (imaginary[reversed], imaginary[i]);
            }
        }

        for (var length = 2; length <= SampleCount; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var stepReal = Math.Cos(angle);
            var stepImaginary = Math.Sin(angle);
            var halfLength = length >> 1;
            for (var offset = 0; offset < SampleCount; offset += length)
            {
                var currentReal = 1d;
                var currentImaginary = 0d;
                for (var index = 0; index < halfLength; index++)
                {
                    var even = offset + index;
                    var odd = even + halfLength;
                    var oddReal = (real[odd] * currentReal) - (imaginary[odd] * currentImaginary);
                    var oddImaginary = (real[odd] * currentImaginary) + (imaginary[odd] * currentReal);
                    real[odd] = real[even] - oddReal;
                    imaginary[odd] = imaginary[even] - oddImaginary;
                    real[even] += oddReal;
                    imaginary[even] += oddImaginary;
                    var nextReal = (currentReal * stepReal) - (currentImaginary * stepImaginary);
                    currentImaginary = (currentReal * stepImaginary) + (currentImaginary * stepReal);
                    currentReal = nextReal;
                }
            }
        }
    }

    private static double FrequencyForBandEdge(int edge)
    {
        const double minFrequency = 60;
        const double maxFrequency = 7800;
        var ratio = edge / (double)BarCount;
        return minFrequency * Math.Pow(maxFrequency / minFrequency, ratio);
    }

    private void SetSpectrum(ReadOnlySpan<double> spectrum)
    {
        lock (_syncRoot)
        {
            for (var i = 0; i < BarCount; i++)
            {
                var target = spectrum.IsEmpty ? 0 : spectrum[i];
                // Fast attack keeps beats immediate; a gentler release removes flicker between samples.
                var response = target > _latestSpectrum[i] ? 0.68 : 0.24;
                _latestSpectrum[i] += (target - _latestSpectrum[i]) * response;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();

        // Wait briefly for the background task to observe cancellation before we
        // dispose the token source. Without this, the background loop can call
        // _disposeCts.Token.ThrowIfCancellationRequested() after the CTS is disposed
        // and throw ObjectDisposedException.
        try
        {
            _captureTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // OperationCanceledException wrapped by Task.Wait — expected on cancellation.
        }

        _disposeCts.Dispose();
    }
}
