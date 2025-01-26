using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace VRCOSC.Modules.OSCAudioReaction;

[ModuleTitle("Audio Direction")]
[ModuleDescription("Sends audio direction and volume to VRChat parameters for stereo visualization")]
[ModuleType(ModuleType.Generic)]
public class OSCAudioDirectionModule : Module
{
    private MMDeviceEnumerator? deviceEnumerator;
    private MMDevice? selectedDevice;
    private WasapiLoopbackCapture? audioCapture;
    private float[]? audioBuffer;
    private readonly object bufferLock = new();
    private bool isRunning;
    private float audioLevel;
    private float audioPeak;
    private int bytesPerSample;
    private float leftRaw;
    private float rightRaw;
    private bool shouldUpdate = true;
    private const float TARGET_LEVEL = 0.5f;  // Target average volume level
    private const float AGC_SPEED = 0.1f;     // How fast AGC adjusts (0-1)
    private float currentGain = 1.0f;         // Current automatic gain value
    
    // Error recovery
    private int errorCount = 0;
    private const int MAX_ERRORS = 3;
    private DateTime lastErrorTime = DateTime.MinValue;
    private const int ERROR_RESET_MS = 5000;
    
    // Smoothing
    private float smoothedVolume = 0f;
    private float smoothedDirection = 0.5f;
    private const int HISTORY_SIZE = 3;
    private readonly Queue<float> volumeHistory = new(HISTORY_SIZE);
    private readonly Queue<float> directionHistory = new(HISTORY_SIZE);
    
    // Parameter rate limiting
    private DateTime lastVolumeUpdate = DateTime.Now;
    private DateTime lastDirectionUpdate = DateTime.Now;
    private const int MIN_UPDATE_MS = 50; // Minimum time between parameter updates

    private enum AudioParameter
    {
        AudioDirection,
        AudioVolume
    }

    private enum AudioSetting
    {
        Gain,
        EnableAGC,
        Smoothing,
        DirectionThreshold
    }

    protected override void OnPreLoad()
    {
        // Add settings
        CreateSlider(AudioSetting.Gain, "Manual Gain", "Manual gain multiplier when AGC is disabled", 1.0f, 0.1f, 5.0f);
        CreateToggle(AudioSetting.EnableAGC, "Auto Gain", "Enable automatic gain control", true);
        CreateSlider(AudioSetting.Smoothing, "Smoothing", "Smoothing factor for volume and direction changes (0 = none, 1 = max)", 0.5f, 0.0f, 1.0f);
        CreateSlider(AudioSetting.DirectionThreshold, "Direction Threshold", "Minimum volume level to calculate direction", 0.01f, 0.001f, 0.1f);

        // Register parameters
        RegisterParameter<float>(AudioParameter.AudioDirection, "audio_direction", ParameterMode.Write, "Audio Direction", "Direction of the sound (0 = left, 0.5 = center, 1 = right)");
        RegisterParameter<float>(AudioParameter.AudioVolume, "audio_volume", ParameterMode.Write, "Audio Volume", "Volume of the sound (0 = silent, 1 = loud)");

        // Initialize history
        for (int i = 0; i < HISTORY_SIZE; i++)
        {
            volumeHistory.Enqueue(0f);
            directionHistory.Enqueue(0.5f);
        }
    }

    private void SetupAudioCapture()
    {
        try
        {
            if (deviceEnumerator == null)
            {
                deviceEnumerator = new MMDeviceEnumerator();
                Debug.WriteLine("[AudioDirection] Created new MMDeviceEnumerator");
            }

            // Get default output device
            var newDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (newDevice == null)
            {
                Debug.WriteLine("[AudioDirection] ERROR: Failed to get default audio device");
                return;
            }

            Debug.WriteLine($"[AudioDirection] Found default device: {newDevice.FriendlyName}");

            // Check if we need to switch devices
            if (selectedDevice != null && selectedDevice.FriendlyName == newDevice.FriendlyName && 
                audioCapture?.CaptureState == CaptureState.Capturing)
            {
                newDevice.Dispose();
                return;
            }

            // Clean up old capture
            if (audioCapture != null)
            {
                Debug.WriteLine("[AudioDirection] Cleaning up old audio capture");
                audioCapture.StopRecording();
                audioCapture.Dispose();
                audioCapture.DataAvailable -= OnDataAvailable;
                audioCapture = null;
            }

            if (selectedDevice != null)
            {
                selectedDevice.Dispose();
                selectedDevice = null;
            }

            // Set up new capture
            selectedDevice = newDevice;
            audioCapture = new WasapiLoopbackCapture(selectedDevice);
            audioCapture.DataAvailable += OnDataAvailable;
            audioCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            bytesPerSample = audioCapture.WaveFormat.BitsPerSample / audioCapture.WaveFormat.BlockAlign;
            
            Debug.WriteLine($"[AudioDirection] Set up new audio capture: {audioCapture.WaveFormat}");
            
            audioCapture.StartRecording();
            isRunning = true;
            errorCount = 0; // Reset error count on successful setup
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioDirection] ERROR: Failed to set up audio capture: {ex.Message}");
            isRunning = false;

            // Handle error recovery
            var now = DateTime.Now;
            if ((now - lastErrorTime).TotalMilliseconds > ERROR_RESET_MS)
            {
                errorCount = 0; // Reset error count if enough time has passed
            }
            
            errorCount++;
            lastErrorTime = now;

            if (errorCount >= MAX_ERRORS)
            {
                Debug.WriteLine("[AudioDirection] ERROR: Too many errors, stopping audio capture");
                return;
            }

            // Try to recover by forcing a new setup after a delay
            Task.Delay(1000).ContinueWith(_ => SetupAudioCapture());
        }
    }

    protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
        switch (parameter.Lookup)
        {
            case AudioParameter.AudioVolume:
                audioLevel = parameter.GetValue<float>();
                break;
            case AudioParameter.AudioDirection:
                audioPeak = parameter.GetValue<float>();
                break;
        }
    }

    [ModuleUpdate(ModuleUpdateMode.Custom, true, 33)]
    private void UpdateAudio()
    {
        if (!isRunning)
        {
            // Try to recover if not running
            var now = DateTime.Now;
            if ((now - lastErrorTime).TotalMilliseconds > ERROR_RESET_MS)
            {
                Debug.WriteLine("[AudioDirection] Attempting to recover audio capture");
                SetupAudioCapture();
            }
            return;
        }
        shouldUpdate = true;
    }

    private float UpdateSmoothed(float newValue, Queue<float> history, float currentSmoothed)
    {
        var smoothing = GetSettingValue<float>(AudioSetting.Smoothing);
        
        // Update history
        history.Dequeue();
        history.Enqueue(newValue);
        
        // Calculate smoothed value
        if (smoothing <= 0) return newValue;
        
        var target = history.Average();
        return currentSmoothed * smoothing + target * (1 - smoothing);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!isRunning || !shouldUpdate) return;
        shouldUpdate = false;

        leftRaw = 0;
        rightRaw = 0;

        // Process stereo float audio
        if (bytesPerSample == 4)  // 32-bit float
        {
            for (int i = 0; i < e.BytesRecorded; i += bytesPerSample * 2)
            {
                leftRaw += Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                rightRaw += Math.Abs(BitConverter.ToSingle(e.Buffer, i + bytesPerSample));
            }
        }

        var leftLevel = leftRaw / (e.BytesRecorded / bytesPerSample);
        var rightLevel = rightRaw / (e.BytesRecorded / bytesPerSample);

        // Boost volume to usable level
        leftLevel *= 10;
        rightLevel *= 10;

        // Apply gain (either automatic or manual)
        var useAGC = GetSettingValue<bool>(AudioSetting.EnableAGC);
        var gain = useAGC ? currentGain : GetSettingValue<float>(AudioSetting.Gain);
        var rawVolume = (leftLevel + rightLevel) / 2;

        // Update automatic gain if enabled
        if (useAGC && rawVolume > 0.01f)  // Only adjust when there's significant audio
        {
            var targetGain = TARGET_LEVEL / rawVolume;
            targetGain = Math.Clamp(targetGain, 0.1f, 5.0f);  // Limit gain range
            currentGain = currentGain * (1 - AGC_SPEED) + targetGain * AGC_SPEED;
        }

        // Apply gain and calculate final volume
        var volume = Math.Min(rawVolume * gain, 1.0f);

        // Calculate direction (0 = left, 0.5 = center, 1 = right)
        var direction = 0.5f;  // Default to center
        var totalLevel = leftLevel + rightLevel;
        var directionThreshold = GetSettingValue<float>(AudioSetting.DirectionThreshold);
        if (totalLevel > directionThreshold)  // Only calculate direction if there's significant audio
        {
            direction = rightLevel / totalLevel;
        }

        // Apply smoothing
        smoothedVolume = UpdateSmoothed(volume, volumeHistory, smoothedVolume);
        smoothedDirection = UpdateSmoothed(direction, directionHistory, smoothedDirection);

        // Clamp values to avoid VRChat bugs
        smoothedVolume = VRCClamp(smoothedVolume);
        smoothedDirection = VRCClamp(smoothedDirection);

        var now = DateTime.Now;

        // Send volume parameter if changed and rate limit passed
        if (Math.Abs(smoothedVolume - audioLevel) > 0.001f && 
            (now - lastVolumeUpdate).TotalMilliseconds >= MIN_UPDATE_MS)
        {
            audioLevel = smoothedVolume;
            SendParameter(AudioParameter.AudioVolume, audioLevel);
            lastVolumeUpdate = now;
        }

        // Send direction parameter if changed and rate limit passed
        if (Math.Abs(smoothedDirection - audioPeak) > 0.001f && 
            (now - lastDirectionUpdate).TotalMilliseconds >= MIN_UPDATE_MS)
        {
            audioPeak = smoothedDirection;
            SendParameter(AudioParameter.AudioDirection, audioPeak);
            lastDirectionUpdate = now;
        }
    }

    protected override async Task<bool> OnModuleStart()
    {
        Debug.WriteLine("[AudioDirection] Starting audio direction module");
        SetupAudioCapture();
        return isRunning;
    }

    protected override async Task OnModuleStop()
    {
        Debug.WriteLine("[AudioDirection] Stopping audio direction module");
        isRunning = false;
        
        if (audioCapture != null)
        {
            audioCapture.StopRecording();
            audioCapture.Dispose();
            audioCapture = null;
        }
        
        if (selectedDevice != null)
        {
            selectedDevice.Dispose();
            selectedDevice = null;
        }
        
        if (deviceEnumerator != null)
        {
            deviceEnumerator.Dispose();
            deviceEnumerator = null;
        }
    }

    private static float VRCClamp(float value)
    {
        return value switch
        {
            < 0.005f => 0.005f,
            > 1f => 1f,
            _ => value
        };
    }
} 