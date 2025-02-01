using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;

namespace VRCOSC.Modules.OSCAudioReaction;

[ModuleTitle("Audio Direction")]
[ModuleDescription("Sends audio direction and volume to VRChat for stereo visualization")]
[ModuleType(ModuleType.Generic)]
public class OSCAudioDirectionModule : Module
{
    private MMDeviceEnumerator? deviceEnumerator;
    private MMDevice? selectedDevice;
    private WasapiLoopbackCapture? audioCapture;
    private bool isRunning;
    private float currentVolume, currentDirection;
    private int bytesPerSample;
    private float leftRaw, rightRaw;
    private volatile bool shouldUpdate = true;
    private float currentGain = 1.0f;
    private float smoothedVolume, smoothedDirection = 0.5f;
    private DateTime lastVolumeUpdate = DateTime.Now;
    private DateTime lastDirectionUpdate = DateTime.Now;

    private const float TARGET_LEVEL = 0.5f;
    private const float AGC_SPEED = 0.1f;
    private const int HISTORY_SIZE = 3;
    private const int MIN_UPDATE_MS = 50;

    private readonly Queue<float> volumeHistory = new(HISTORY_SIZE);
    private readonly Queue<float> directionHistory = new(HISTORY_SIZE);

    private enum AudioParameter { AudioDirection, AudioVolume }
    private enum AudioSetting { Gain, EnableAGC, Smoothing, DirectionThreshold }

    protected override void OnPreLoad()
    {
        CreateSlider(AudioSetting.Gain, "Manual Gain", "Manual gain multiplier", 1.0f, 0.1f, 5.0f);
        CreateToggle(AudioSetting.EnableAGC, "Auto Gain", "Enable AGC", true);
        CreateSlider(AudioSetting.Smoothing, "Smoothing", "Smoothing factor (0 = none, 1 = max)", 0.5f, 0.0f, 1.0f);
        CreateSlider(AudioSetting.DirectionThreshold, "Direction Threshold", "Minimum volume for direction", 0.01f, 0.0f, 0.1f, 0.005f);

        RegisterParameter<float>(AudioParameter.AudioDirection, "audio_direction", ParameterMode.Write, "Audio Direction", "0=left, 0.5=center, 1=right");
        RegisterParameter<float>(AudioParameter.AudioVolume, "audio_volume", ParameterMode.Write, "Audio Volume", "0=silent, 1=loud");

        for (int i = 0; i < HISTORY_SIZE; i++) {
            volumeHistory.Enqueue(0f);
            directionHistory.Enqueue(0.5f);
        }
    }

    protected override async Task<bool> OnModuleStart()
    {
        SetupAudioCapture();
        return isRunning;
    }

    protected override async Task OnModuleStop()
    {
        isRunning = false;
        CleanupAudioCapture();
        deviceEnumerator?.Dispose();
        deviceEnumerator = null;
    }

    protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
        switch ((AudioParameter)parameter.Lookup)
        {
            case AudioParameter.AudioVolume:
                currentVolume = parameter.GetValue<float>();
                break;
            case AudioParameter.AudioDirection:
                currentDirection = parameter.GetValue<float>();
                break;
        }
    }

    [ModuleUpdate(ModuleUpdateMode.Custom, true, 33)]
    private void UpdateAudio()
    {
        if (!isRunning) {
            SetupAudioCapture();
            return;
        }
        shouldUpdate = true;
    }

    private float UpdateSmoothed(float newValue, Queue<float> history, float currentSmoothed)
    {
        var smoothing = GetSettingValue<float>(AudioSetting.Smoothing);
        history.Dequeue();
        history.Enqueue(newValue);
        return smoothing <= 0 ? newValue : currentSmoothed * smoothing + history.Average() * (1 - smoothing);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!isRunning || !shouldUpdate) return;
        shouldUpdate = false;

        leftRaw = rightRaw = 0;
        if (bytesPerSample == 4) {
            for (int i = 0; i < e.BytesRecorded; i += bytesPerSample * 2) {
                leftRaw += Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                rightRaw += Math.Abs(BitConverter.ToSingle(e.Buffer, i + bytesPerSample));
            }
        }

        var leftLevel = 10 * leftRaw / (e.BytesRecorded / bytesPerSample);
        var rightLevel = 10 * rightRaw / (e.BytesRecorded / bytesPerSample);
        var rawVolume = (leftLevel + rightLevel) / 2;

        var useAGC = GetSettingValue<bool>(AudioSetting.EnableAGC);
        if (useAGC && rawVolume > 0.01f) {
            var targetGain = Math.Clamp(TARGET_LEVEL / rawVolume, 0.1f, 5.0f);
            currentGain = currentGain * (1 - AGC_SPEED) + targetGain * AGC_SPEED;
        }

        var volume = Math.Min(rawVolume * (useAGC ? currentGain : GetSettingValue<float>(AudioSetting.Gain)), 1.0f);
        var direction = 0.5f;
        var totalLevel = leftLevel + rightLevel;

        if (totalLevel > GetSettingValue<float>(AudioSetting.DirectionThreshold))
            direction = rightLevel / totalLevel;

        smoothedVolume = VRCClamp(UpdateSmoothed(volume, volumeHistory, smoothedVolume));
        smoothedDirection = VRCClamp(UpdateSmoothed(direction, directionHistory, smoothedDirection));

        var now = DateTime.Now;
        if (Math.Abs(smoothedVolume - currentVolume) > 0.001f && (now - lastVolumeUpdate).TotalMilliseconds >= MIN_UPDATE_MS) {
            currentVolume = smoothedVolume;
            SendParameter(AudioParameter.AudioVolume, currentVolume);
            lastVolumeUpdate = now;
        }

        if (Math.Abs(smoothedDirection - currentDirection) > 0.001f && (now - lastDirectionUpdate).TotalMilliseconds >= MIN_UPDATE_MS) {
            currentDirection = smoothedDirection;
            SendParameter(AudioParameter.AudioDirection, currentDirection);
            lastDirectionUpdate = now;
        }
    }

    private void SetupAudioCapture()
    {
        try {
            deviceEnumerator ??= new MMDeviceEnumerator();
            var newDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            
            if (selectedDevice?.FriendlyName == newDevice.FriendlyName && 
                audioCapture?.CaptureState == CaptureState.Capturing) {
                newDevice.Dispose();
                return;
            }

            CleanupAudioCapture();
            selectedDevice = newDevice;
            audioCapture = new WasapiLoopbackCapture(selectedDevice);
            audioCapture.DataAvailable += OnDataAvailable;
            audioCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            bytesPerSample = audioCapture.WaveFormat.BitsPerSample / audioCapture.WaveFormat.BlockAlign;
            audioCapture.StartRecording();
            isRunning = true;
        }
        catch (Exception ex) {
            Debug.WriteLine($"[AudioDirection] Setup error: {ex.Message}");
            isRunning = false;
        }
    }

    private void CleanupAudioCapture()
    {
        try {
            if (audioCapture != null) {
                audioCapture.StopRecording();
                audioCapture.DataAvailable -= OnDataAvailable;
                audioCapture.Dispose();
                audioCapture = null;
            }
            if (selectedDevice != null) {
                selectedDevice.Dispose();
                selectedDevice = null;
            }
        }
        catch (Exception ex) {
            Debug.WriteLine($"[AudioDirection] Cleanup error: {ex.Message}");
        }
    }

    private static float VRCClamp(float value) => 
        value < 0.005f ? 0.005f : value > 1f ? 1f : value;
} 