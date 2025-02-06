using System.Collections.Generic;

namespace CrookedToe.Modules.OSCAudioReaction.AudioProcessing;

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
    public const float DEFAULT_SPIKE_THRESHOLD = 2.0f;  // 200% increase
    public const float MIN_SPIKE_THRESHOLD = 0.5f;      // 50% increase
    public const float MAX_SPIKE_THRESHOLD = 5.0f;      // 500% increase
    
    // FFT Size Options
    public const int FFT_SIZE_LOW = 4096;     // ~11.7Hz resolution
    public const int FFT_SIZE_MEDIUM = 8192;  // ~5.86Hz resolution
    public const int FFT_SIZE_HIGH = 16384;   // ~2.93Hz resolution
    public const int DEFAULT_FFT_SIZE = FFT_SIZE_MEDIUM;

    // Frequency Band Ranges
    public static readonly (float low, float high, string name)[] FrequencyBands = 
    {
        (20, 60, "Sub Bass"),
        (60, 250, "Bass"),
        (250, 500, "Low Mid"),
        (500, 2000, "Mid"),
        (2000, 4000, "Upper Mid"),
        (4000, 6000, "Presence"),
        (6000, 25000, "Brilliance")
    };
}

public record AudioConfiguration : IAudioConfiguration
{
    public float Gain { get; init; } = AudioConstants.DEFAULT_GAIN;
    public bool EnableAGC { get; init; } = true;
    public float Smoothing { get; init; } = 0.5f;
    public float DirectionThreshold { get; init; } = 0.01f;
    public int UpdateIntervalMs { get; init; } = AudioConstants.MIN_UPDATE_MS;
    public string? PreferredDeviceId { get; init; }
    public int FrequencyBands { get; init; } = AudioConstants.DEFAULT_FREQUENCY_BANDS;
    public float FrequencySmoothing { get; init; } = AudioConstants.DEFAULT_FREQUENCY_SMOOTHING;
    public int FftSize { get; init; } = AudioConstants.DEFAULT_FFT_SIZE;
    public float SpikeThreshold { get; init; } = AudioConstants.DEFAULT_SPIKE_THRESHOLD;
    
    public static AudioConfiguration Default => new();
} 