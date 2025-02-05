using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;
using VRCOSC.Modules.OSCAudioReaction.AudioProcessing;

namespace VRCOSC.Modules.OSCAudioReaction;

[ModuleTitle("Audio Direction")]
[ModuleDescription("Sends audio direction and volume to VRChat for stereo visualization")]
[ModuleType(ModuleType.Generic)]
public class OSCAudioDirectionModule : Module, IDisposable
{
    private readonly IAudioFactory _audioFactory;
    private IAudioProcessor? _audioProcessor;
    private IAudioDeviceManager? _deviceManager;
    private IAudioConfiguration _config;
    private DateTime _lastVolumeUpdate = DateTime.Now;
    private DateTime _lastDirectionUpdate = DateTime.Now;
    private float _currentVolume, _currentDirection;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private bool _isDisposed;
    private readonly object _configLock = new object();
    private volatile bool _configurationChanged = false;

    private enum AudioParameter 
    { 
        AudioDirection, 
        AudioVolume,
        AudioSpike,
        // Individual frequency band parameters
        SubBassVolume,    // 16-60Hz
        BassVolume,       // 60-250Hz
        LowMidVolume,     // 250-500Hz
        MidVolume,        // 500Hz-2kHz
        UpperMidVolume,   // 2-4kHz
        PresenceVolume,   // 4-6kHz
        BrillianceVolume  // 6-20kHz
    }

    private enum AudioSetting 
    { 
        Gain, 
        EnableAGC, 
        Smoothing, 
        DirectionThreshold,
        PresetSelection,
        ScaleFrequencyWithVolume,
        SpikeThreshold,
        // Frequency band toggles
        EnableSubBass,      // 16-60Hz
        EnableBass,         // 60-250Hz
        EnableLowMid,       // 250-500Hz
        EnableMid,          // 500Hz-2kHz
        EnableUpperMid,     // 2-4kHz
        EnablePresence,     // 4-6kHz
        EnableBrilliance,   // 6-20kHz
        FrequencySmoothing  // Smoothing for frequency analysis
    }

    private enum PresetSelection
    {
        Custom,
        Default,         // Balanced settings for most use cases
        LowLatency,     // For real-time responsiveness
        VoiceOptimized, // For speech and vocal pattern detection
        HighSmoothing,  // For stable, gradual reactions
        MusicOptimized  // For musical performance and visualization
    }

    public OSCAudioDirectionModule()
    {
        _audioFactory = new AudioFactory();
        _config = new AudioConfiguration();
    }

    protected override void OnPreLoad()
    {
        // Basic settings
        CreateDropdown(AudioSetting.PresetSelection, "Avatar Preset", 
            "Select a preset for your avatar's audio reactions. Each preset has optimized settings for different use cases.", 
            PresetSelection.Default);
        
        CreateToggle(AudioSetting.EnableAGC, "Auto Gain Control", 
            "Automatically adjusts gain to maintain consistent volume levels. Target level: 0.5", 
            true);
        
        CreateToggle(AudioSetting.ScaleFrequencyWithVolume, "Scale Frequencies with Volume", 
            "When enabled, frequency bands are scaled by audio volume. When disabled, they show pure proportions (sum to 1)", 
            true);
        
        CreateSlider(AudioSetting.SpikeThreshold, "Spike Sensitivity", 
            "Threshold for volume spike detection (0.5 = very sensitive, 5.0 = less sensitive). Note: Can be finnicky, works best with sharp volume changes.", 
            AudioConstants.DEFAULT_SPIKE_THRESHOLD, 
            AudioConstants.MIN_SPIKE_THRESHOLD, 
            AudioConstants.MAX_SPIKE_THRESHOLD);

        // Custom Preset Settings
        CreateSlider(AudioSetting.Gain, "Custom: Gain", 
            "Manual volume multiplier when using Custom preset or AGC is disabled (0.1 = quiet, 5.0 = loud)", 
            1.0f, 0.1f, 5.0f);
        
        CreateSlider(AudioSetting.Smoothing, "Custom: Animation Smoothing", 
            "Smoothing factor for avatar animations in Custom preset (0 = immediate response but jittery, 1 = smooth but delayed)", 
            0.5f, 0.0f, 1.0f);
        
        CreateSlider(AudioSetting.DirectionThreshold, "Custom: Direction Sensitivity", 
            "Minimum volume required to update direction. Lower values are more responsive but may be jittery", 
            0.01f, 0.0f, 0.1f, 0.005f);
        
        CreateSlider(AudioSetting.FrequencySmoothing, "Custom: Frequency Smoothing", 
            "Smoothing applied to frequency band values. Higher values reduce flickering but increase latency", 
            0.5f, 0.0f, 1.0f);

        // Frequency band toggles with detailed descriptions
        CreateToggle(AudioSetting.EnableSubBass, "Sub Bass (16-60Hz)", 
            "Very low frequencies: bass drops, explosions, rumble effects", false);
        
        CreateToggle(AudioSetting.EnableBass, "Bass (60-250Hz)", 
            "Low frequencies: bass guitar, kick drums, male vocals", true);
        
        CreateToggle(AudioSetting.EnableLowMid, "Low Mid (250-500Hz)", 
            "Lower midrange: bass line melodies, lower harmonics", true);
        
        CreateToggle(AudioSetting.EnableMid, "Mid (500Hz-2kHz)", 
            "Main vocal range, most instruments' fundamental frequencies", true);
        
        CreateToggle(AudioSetting.EnableUpperMid, "Upper Mid (2-4kHz)", 
            "Presence range: vocal clarity, instrument attack sounds", false);
        
        CreateToggle(AudioSetting.EnablePresence, "Presence (4-6kHz)", 
            "Definition range: speech intelligibility, high harmonics", false);
        
        CreateToggle(AudioSetting.EnableBrilliance, "Brilliance (6-20kHz)", 
            "Air frequencies: cymbals, sibilance, sparkle", false);

        // Main parameters
        RegisterParameter<float>(AudioParameter.AudioDirection, "audio_direction", 
            ParameterMode.Write, "Audio Direction", "0=left, 0.5=center, 1=right");
        
        RegisterParameter<float>(AudioParameter.AudioVolume, "audio_volume", 
            ParameterMode.Write, "Audio Volume", "0=silent, 1=loud");
        
        RegisterParameter<bool>(AudioParameter.AudioSpike, "audio_spike", 
            ParameterMode.Write, "Audio Spike", "True when a sudden volume increase is detected. Note: Can be inconsistent.");

        // Frequency band parameters
        RegisterParameter<float>(AudioParameter.SubBassVolume, "audio_subbass", 
            ParameterMode.Write, "Sub Bass Volume (16-60Hz)", "Very low frequencies: bass drops, explosions, rumble");
        
        RegisterParameter<float>(AudioParameter.BassVolume, "audio_bass", 
            ParameterMode.Write, "Bass Volume (60-250Hz)", "Low frequencies: bass guitar, kick drums, male vocals");
        
        RegisterParameter<float>(AudioParameter.LowMidVolume, "audio_lowmid", 
            ParameterMode.Write, "Low Mid Volume (250-500Hz)", "Lower midrange: bass line melodies, lower harmonics");
        
        RegisterParameter<float>(AudioParameter.MidVolume, "audio_mid", 
            ParameterMode.Write, "Mid Volume (500Hz-2kHz)", "Main vocal range, most instruments");
        
        RegisterParameter<float>(AudioParameter.UpperMidVolume, "audio_uppermid", 
            ParameterMode.Write, "Upper Mid Volume (2-4kHz)", "Presence range: vocal clarity, attack sounds");
        
        RegisterParameter<float>(AudioParameter.PresenceVolume, "audio_presence", 
            ParameterMode.Write, "Presence Volume (4-6kHz)", "Definition range: speech intelligibility, high harmonics");
        
        RegisterParameter<float>(AudioParameter.BrillianceVolume, "audio_brilliance", 
            ParameterMode.Write, "Brilliance Volume (6-20kHz)", "Air frequencies: cymbals, sibilance, sparkle");

        // Create groups
        CreateGroup("Basic Settings", 
            AudioSetting.PresetSelection,
            AudioSetting.SpikeThreshold,
            AudioSetting.EnableAGC,
            AudioSetting.ScaleFrequencyWithVolume);
        
        CreateGroup("Custom Preset Settings", 
            AudioSetting.Gain, AudioSetting.Smoothing, 
            AudioSetting.DirectionThreshold, AudioSetting.FrequencySmoothing);
        
        CreateGroup("Frequency Bands", 
            AudioSetting.EnableSubBass, AudioSetting.EnableBass, AudioSetting.EnableLowMid, 
            AudioSetting.EnableMid, AudioSetting.EnableUpperMid, AudioSetting.EnablePresence, 
            AudioSetting.EnableBrilliance);
    }

    protected override async Task<bool> OnModuleStart()
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            UpdateConfigurationFromSettings();
            _deviceManager = _audioFactory.CreateDeviceManager(_config);
            
            if (!await _deviceManager.InitializeDefaultDeviceAsync())
            {
                Log("Failed to initialize audio device");
                return false;
            }

            _audioProcessor = _audioFactory.CreateProcessor(_config, _deviceManager.AudioCapture!.WaveFormat.BitsPerSample / 8);
            _deviceManager.DataAvailable += OnDataAvailable;
            
            return true;
        }
        catch (AudioDeviceException ex)
        {
            Log($"Audio device error: {ex.Message}");
            return false;
        }
        catch (AudioProcessingException ex)
        {
            Log($"Audio processing error: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"Unexpected error: {ex.Message}");
            return false;
        }
    }

    protected override async Task OnModuleStop()
    {
        try
        {
            if (_cancellationTokenSource != null)
            {
                await _cancellationTokenSource.CancelAsync();
            }

            // Stop audio processing first
            if (_deviceManager != null)
            {
                _deviceManager.DataAvailable -= OnDataAvailable;
                _deviceManager.StopCapture();
            }

            // Wait for any ongoing processing to complete
            await _processingLock.WaitAsync();
            try
            {
                if (_audioProcessor != null)
                {
                    _audioProcessor.Dispose();
                    _audioProcessor = null;
                }

                if (_deviceManager != null)
                {
                    _deviceManager.Dispose();
                    _deviceManager = null;
                }

                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
            }
            finally
            {
                _processingLock.Release();
            }
        }
        catch (Exception ex)
        {
            Log($"Error during module stop: {ex.Message}");
        }
    }

    private void UpdateConfigurationFromSettings()
    {
        lock (_configLock)
        {
            var selectedPreset = GetSettingValue<PresetSelection>(AudioSetting.PresetSelection);
            
            AudioConfiguration newConfig;
            
            // First apply the main preset
            switch (selectedPreset)
            {
                case PresetSelection.Default:
                    newConfig = new AudioConfiguration
                    {
                        Gain = 1.0f,
                        Smoothing = 0.5f,            // Balanced smoothing
                        DirectionThreshold = 0.01f,
                        FrequencySmoothing = GetSettingValue<float>(AudioSetting.FrequencySmoothing),
                        FftSize = AudioConstants.DEFAULT_FFT_SIZE,
                        SpikeThreshold = GetSettingValue<float>(AudioSetting.SpikeThreshold)
                    };
                    break;

                case PresetSelection.LowLatency:
                    newConfig = new AudioConfiguration
                    {
                        Gain = 1.2f,
                        Smoothing = 0.3f,            // Quick response time
                        DirectionThreshold = 0.01f,
                        FrequencySmoothing = GetSettingValue<float>(AudioSetting.FrequencySmoothing),
                        FftSize = AudioConstants.FFT_SIZE_LOW,
                        SpikeThreshold = GetSettingValue<float>(AudioSetting.SpikeThreshold)
                    };
                    break;

                case PresetSelection.VoiceOptimized:
                    newConfig = new AudioConfiguration
                    {
                        Gain = 1.5f,
                        Smoothing = 0.4f,            // Balanced for speech
                        DirectionThreshold = 0.02f,
                        FrequencySmoothing = GetSettingValue<float>(AudioSetting.FrequencySmoothing),
                        FftSize = AudioConstants.FFT_SIZE_LOW,
                        SpikeThreshold = GetSettingValue<float>(AudioSetting.SpikeThreshold)
                    };
                    break;

                case PresetSelection.HighSmoothing:
                    newConfig = new AudioConfiguration
                    {
                        Gain = 1.0f,
                        Smoothing = 0.8f,            // Maximum smoothing
                        DirectionThreshold = 0.015f,
                        FrequencySmoothing = GetSettingValue<float>(AudioSetting.FrequencySmoothing),
                        FftSize = AudioConstants.FFT_SIZE_HIGH,
                        SpikeThreshold = GetSettingValue<float>(AudioSetting.SpikeThreshold)
                    };
                    break;

                case PresetSelection.MusicOptimized:
                    newConfig = new AudioConfiguration
                    {
                        Gain = 1.1f,
                        Smoothing = 0.5f,            // Balanced for music
                        DirectionThreshold = 0.01f,
                        FrequencySmoothing = GetSettingValue<float>(AudioSetting.FrequencySmoothing),
                        FftSize = AudioConstants.FFT_SIZE_MEDIUM,
                        SpikeThreshold = GetSettingValue<float>(AudioSetting.SpikeThreshold)
                    };
                    break;

                case PresetSelection.Custom:
                default:
                    // Create new config with current values
                    newConfig = new AudioConfiguration
                    {
                        Gain = GetSettingValue<float>(AudioSetting.Gain),
                        Smoothing = GetSettingValue<float>(AudioSetting.Smoothing),
                        DirectionThreshold = GetSettingValue<float>(AudioSetting.DirectionThreshold),
                        FrequencySmoothing = GetSettingValue<float>(AudioSetting.FrequencySmoothing),
                        SpikeThreshold = GetSettingValue<float>(AudioSetting.SpikeThreshold),
                        FftSize = _config.FftSize,
                        UpdateIntervalMs = _config.UpdateIntervalMs,
                        PreferredDeviceId = _config.PreferredDeviceId,
                        FrequencyBands = _config.FrequencyBands
                    };
                    break;
            }

            // Apply global settings that override presets
            newConfig.EnableAGC = GetSettingValue<bool>(AudioSetting.EnableAGC);
            newConfig.FrequencyBands = 8;
            newConfig.SpikeThreshold = GetSettingValue<float>(AudioSetting.SpikeThreshold);

            // Only update if there are actual changes
            if (!ConfigurationsEqual(newConfig, _config))
            {
                _config = newConfig;
                _configurationChanged = true;
            }
        }
    }

    private bool ConfigurationsEqual(IAudioConfiguration a, IAudioConfiguration b)
    {
        return a.Gain == b.Gain &&
               a.EnableAGC == b.EnableAGC &&
               a.Smoothing == b.Smoothing &&
               a.DirectionThreshold == b.DirectionThreshold &&
               a.FrequencySmoothing == b.FrequencySmoothing &&
               a.SpikeThreshold == b.SpikeThreshold &&
               a.FftSize == b.FftSize;
    }

    private void ApplyConfigurationToProcessor()
    {
        if (_audioProcessor == null || !_configurationChanged) return;

        lock (_configLock)
        {
            try
            {
                _audioProcessor.UpdateGain(_config.Gain);
                _audioProcessor.UpdateSmoothing(_config.Smoothing);

                var enabledBands = new bool[_config.FrequencyBands];
                if (enabledBands.Length >= 7)
                {
                    enabledBands[0] = GetSettingValue<bool>(AudioSetting.EnableSubBass);
                    enabledBands[1] = GetSettingValue<bool>(AudioSetting.EnableBass);
                    enabledBands[2] = GetSettingValue<bool>(AudioSetting.EnableLowMid);
                    enabledBands[3] = GetSettingValue<bool>(AudioSetting.EnableMid);
                    enabledBands[4] = GetSettingValue<bool>(AudioSetting.EnableUpperMid);
                    enabledBands[5] = GetSettingValue<bool>(AudioSetting.EnablePresence);
                    enabledBands[6] = GetSettingValue<bool>(AudioSetting.EnableBrilliance);
                }
                _audioProcessor.ConfigureFrequencyBands(_config.FrequencySmoothing, enabledBands);
                _configurationChanged = false;
            }
            catch (Exception ex)
            {
                Log($"Error applying configuration: {ex.Message}");
            }
        }
    }

    protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
        switch ((AudioParameter)parameter.Lookup)
        {
            case AudioParameter.AudioDirection:
                _currentDirection = parameter.GetValue<float>();
                break;
            case AudioParameter.AudioVolume:
                _currentVolume = parameter.GetValue<float>();
                break;
        }
    }

    private async void OnDataAvailable(object? sender, NAudio.Wave.WaveInEventArgs e)
    {
        if (_isDisposed || _audioProcessor == null || !_audioProcessor.IsActive || 
            _cancellationTokenSource?.Token.IsCancellationRequested == true) 
        {
            return;
        }

        try
        {
            // Try to acquire the lock with a shorter timeout
            if (!await _processingLock.WaitAsync(TimeSpan.FromMilliseconds(10)))
            {
                return;  // Skip this update if we can't get the lock quickly
            }

            try
            {
                // Apply any pending configuration changes before processing
                ApplyConfigurationToProcessor();

                bool scaleWithVolume = GetSettingValue<bool>(AudioSetting.ScaleFrequencyWithVolume);
                var (bands, volume, direction, spike) = _audioProcessor.ProcessAudioData(e, scaleWithVolume);

                var now = DateTime.Now;

                // Update volume parameter
                if (Math.Abs(volume - _currentVolume) > 0.0001f && 
                    (now - _lastVolumeUpdate).TotalMilliseconds >= _config.UpdateIntervalMs)
                {
                    _currentVolume = volume;
                    SendParameter(AudioParameter.AudioVolume, _currentVolume);
                    _lastVolumeUpdate = now;
                }

                // Update direction parameter
                if (Math.Abs(direction - _currentDirection) > 0.0001f &&
                    (now - _lastDirectionUpdate).TotalMilliseconds >= _config.UpdateIntervalMs)
                {
                    _currentDirection = direction;
                    SendParameter(AudioParameter.AudioDirection, _currentDirection);
                    _lastDirectionUpdate = now;

                }

                // Update frequency band parameters
                if (bands.Length >= 7)  // Ensure we have enough bands
                {
                    SendBandParameter(AudioParameter.SubBassVolume, AudioSetting.EnableSubBass, bands[0]);
                    SendBandParameter(AudioParameter.BassVolume, AudioSetting.EnableBass, bands[1]);
                    SendBandParameter(AudioParameter.LowMidVolume, AudioSetting.EnableLowMid, bands[2]);
                    SendBandParameter(AudioParameter.MidVolume, AudioSetting.EnableMid, bands[3]);
                    SendBandParameter(AudioParameter.UpperMidVolume, AudioSetting.EnableUpperMid, bands[4]);
                    SendBandParameter(AudioParameter.PresenceVolume, AudioSetting.EnablePresence, bands[5]);
                    SendBandParameter(AudioParameter.BrillianceVolume, AudioSetting.EnableBrilliance, bands[6]);
                }
                else
                {
                    LogDebug($"Insufficient frequency bands: {bands.Length}");
                }

                // Update spike parameter
                SendParameter(AudioParameter.AudioSpike, spike);
                if (spike)
                {
                    LogDebug($"Spike detected and sent to VRChat!");
                }
            }
            finally
            {
                _processingLock.Release();
            }
        }
        catch (Exception ex)
        {
            if (!_isDisposed)  // Only attempt restart if not disposed
            {
                Log($"Error in audio processing: {ex.Message}");
                try
                {
                    await RestartAudioDevice();
                }
                catch (Exception restartEx)
                {
                    Log($"Failed to restart audio device: {restartEx.Message}");
                }
            }
        }
    }

    private async Task RestartAudioDevice()
    {
        LogDebug("Attempting to restart audio device");
        await OnModuleStop();
        await Task.Delay(100); // Give device time to clean up
        if (!_isDisposed) // Only restart if not disposed
        {
            await OnModuleStart();
        }
    }

    private void SendBandParameter(AudioParameter parameter, AudioSetting enableSetting, float bandValue)
    {
        if (GetSettingValue<bool>(enableSetting))
        {
            float value = Math.Min(bandValue, 1.0f);
            value = (float)Math.Round(value, 4); // Round to 4 significant figures
            SendParameter(parameter, value);
        }
        else
            SendParameter(parameter, 0f);
    }

    [ModuleUpdate(ModuleUpdateMode.Custom, true, 33)]
    private async Task UpdateAudio()
    {
        if (_isDisposed || _cancellationTokenSource?.Token.IsCancellationRequested == true)
            return;

        try
        {
            if (_deviceManager == null || !_deviceManager.IsInitialized)
            {
                LogDebug("Audio device needs initialization");
                await RestartAudioDevice();
                return;
            }

            // Only update configuration from settings, don't apply it here
            UpdateConfigurationFromSettings();
        }
        catch (Exception ex)
        {
            Log($"Error in update loop: {ex.Message}");
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                // Ensure module is stopped
                OnModuleStop().Wait();
                
                // Clean up remaining resources
                _processingLock.Dispose();
                
                // Clear any remaining references
                _audioProcessor = null;
                _deviceManager = null;
                _cancellationTokenSource = null;
            }
            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~OSCAudioDirectionModule()
    {
        Dispose(false);
    }
} 