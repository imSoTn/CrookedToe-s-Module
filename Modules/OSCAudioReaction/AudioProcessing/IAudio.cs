using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VRCOSC.Modules.OSCAudioReaction.AudioProcessing;

public interface IAudioConfiguration
{
    float Gain { get; set; }
    bool EnableAGC { get; set; }
    float Smoothing { get; set; }
    float DirectionThreshold { get; set; }
    int UpdateIntervalMs { get; set; }
    string? PreferredDeviceId { get; set; }
    int FrequencyBands { get; set; }
    float FrequencySmoothing { get; set; }
    int FftSize { get; set; }
    float SpikeThreshold { get; set; }
}

public interface IAudioDeviceManager : IDisposable
{
    bool IsInitialized { get; }
    string? CurrentDeviceName { get; }
    WasapiLoopbackCapture? AudioCapture { get; }
    Task<bool> InitializeDefaultDeviceAsync();
    Task<bool> InitializeDeviceAsync(string deviceId);
    void StopCapture();
    IEnumerable<MMDevice> GetAvailableDevices();
    event EventHandler<WaveInEventArgs>? DataAvailable;
}

public interface IAudioProcessor : IDisposable
{
    float CurrentVolume { get; }
    float CurrentDirection { get; }
    float CurrentGain { get; }
    float CurrentRms { get; }
    float[] FrequencyBands { get; }
    bool IsActive { get; }
    bool HasSpike { get; }

    (float[] bands, float volume, float direction, bool spike) ProcessAudioData(WaveInEventArgs e, bool scaleWithVolume);
    void Reset();
    void UpdateEnabledBands(bool[] enabledBands);
    void UpdateGain(float gain);
    void UpdateSmoothing(float smoothing);
    void ConfigureFrequencyBands(float smoothing, bool[] enabledBands);
}

public interface IAudioFactory
{
    IAudioProcessor CreateProcessor(IAudioConfiguration config, int bytesPerSample);
    IAudioDeviceManager CreateDeviceManager(IAudioConfiguration config);
}

[Serializable]
public class AudioDeviceException : Exception
{
    public AudioDeviceException() { }
    public AudioDeviceException(string message) : base(message) { }
    public AudioDeviceException(string message, Exception inner) : base(message, inner) { }
}

[Serializable]
public class AudioProcessingException : Exception
{
    public AudioProcessingException() { }
    public AudioProcessingException(string message) : base(message) { }
    public AudioProcessingException(string message, Exception inner) : base(message, inner) { }
}

[Serializable]
public class AudioConfigurationException : Exception
{
    public AudioConfigurationException() { }
    public AudioConfigurationException(string message) : base(message) { }
    public AudioConfigurationException(string message, Exception inner) : base(message, inner) { }
} 