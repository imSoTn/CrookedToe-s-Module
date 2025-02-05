using System.Collections.Generic;

namespace VRCOSC.Modules.OSCAudioReaction.AudioProcessing;

public static class AudioConstants
{
    // Audio Configuration
    public const int DEFAULT_SAMPLE_RATE = 48000;
    public const int DEFAULT_CHANNELS = 2;
    public const float DEFAULT_GAIN = 1.0f;
    public const float TARGET_LEVEL = 0.5f;
    public const float AGC_SPEED = 0.1f;
    public const int HISTORY_SIZE = 3;
    public const int MIN_UPDATE_MS = 50;
    public const float MIN_DIRECTION_THRESHOLD = 0.005f;
    public const float MAX_GAIN = 5.0f;
    public const float MIN_GAIN = 0.1f;
    public const int DEFAULT_FREQUENCY_BANDS = 8;
    public const float DEFAULT_FREQUENCY_SMOOTHING = 0.7f;
    public const float DEFAULT_SPIKE_THRESHOLD = 2.0f;  // 200% increase (volume tripling)
    public const float MIN_SPIKE_THRESHOLD = 0.5f;     // 50% increase (volume increasing by half)
    public const float MAX_SPIKE_THRESHOLD = 5.0f;     // 500% increase (volume increasing 6x)
    
    // FFT Size Options
    public const int FFT_SIZE_LOW = 4096;    // Better real-time response, ~11.7Hz resolution
    public const int FFT_SIZE_MEDIUM = 8192;  // Balanced, ~5.86Hz resolution
    public const int FFT_SIZE_HIGH = 16384;   // Better frequency resolution, ~2.93Hz resolution
    public const int DEFAULT_FFT_SIZE = FFT_SIZE_MEDIUM;

    // Frequency Band Ranges
    public static readonly (float low, float high, string name, string description)[] FrequencyBands = 
    {
        (16, 60, "Sub Bass", "Low musical range - upright bass, tuba, bass guitar"),
        (60, 250, "Bass", "Normal speaking range, fundamental bass frequencies"),
        (250, 500, "Low Mid", "Brass instruments, mid woodwinds, alto saxophone"),
        (500, 2000, "Mid", "Higher fundamentals - violin, piccolo"),
        (2000, 4000, "Upper Mid", "Harmonics of lower instruments, trumpet harmonics"),
        (4000, 6000, "Presence", "Violin and piccolo harmonics, vocal clarity"),
        (6000, 20000, "Brilliance", "Sibilant sounds, cymbal harmonics, high-pitched detail")
    };
}

public class AudioConfiguration : IAudioConfiguration
{
    public float Gain { get; set; } = AudioConstants.DEFAULT_GAIN;
    public bool EnableAGC { get; set; } = true;
    public float Smoothing { get; set; } = 0.5f;
    public float DirectionThreshold { get; set; } = 0.01f;
    public int UpdateIntervalMs { get; set; } = AudioConstants.MIN_UPDATE_MS;
    public string? PreferredDeviceId { get; set; }
    public int FrequencyBands { get; set; } = AudioConstants.DEFAULT_FREQUENCY_BANDS;
    public float FrequencySmoothing { get; set; } = AudioConstants.DEFAULT_FREQUENCY_SMOOTHING;
    public int FftSize { get; set; } = AudioConstants.DEFAULT_FFT_SIZE;
    public float SpikeThreshold { get; set; } = AudioConstants.DEFAULT_SPIKE_THRESHOLD;
    
    public static AudioConfiguration Default => new();

    public AudioConfiguration Clone()
    {
        return new AudioConfiguration
        {
            Gain = Gain,
            EnableAGC = EnableAGC,
            Smoothing = Smoothing,
            DirectionThreshold = DirectionThreshold,
            UpdateIntervalMs = UpdateIntervalMs,
            PreferredDeviceId = PreferredDeviceId,
            FrequencyBands = FrequencyBands,
            FrequencySmoothing = FrequencySmoothing,
            FftSize = FftSize,
            SpikeThreshold = SpikeThreshold
        };
    }
} 