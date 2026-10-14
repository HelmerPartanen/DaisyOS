namespace DaisyOS.Core.Services;

public interface IAudioSpectrumService : IDisposable
{
    /// <summary>Copies the latest analyser values into a caller-owned buffer without allocating.</summary>
    void CopySpectrum(Span<double> destination);
}
