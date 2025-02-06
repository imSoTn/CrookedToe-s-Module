using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VRCOSC.App.SDK.Modules;
namespace CrookedToe.Modules.OSCAudioReaction.AudioProcessing;

public class AudioDeviceManager : IAudioDeviceManager
{
    private readonly IAudioConfiguration _config;
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private MMDevice? _selectedDevice;
    private WasapiLoopbackCapture? _audioCapture;
    private bool _isDisposed;
    private readonly OSCAudioDirectionModule _module;

    public bool IsInitialized => _audioCapture?.CaptureState == CaptureState.Capturing;
    public string? CurrentDeviceName => _selectedDevice?.FriendlyName;
    public WasapiLoopbackCapture? AudioCapture => _audioCapture;
    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public AudioDeviceManager(IAudioConfiguration config, OSCAudioDirectionModule module)
    {
        _config = config;
        _module = module;
        _deviceEnumerator = new MMDeviceEnumerator();
    }

    public async Task<bool> InitializeDefaultDeviceAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        
        try
        {
            var newDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_selectedDevice?.FriendlyName == newDevice.FriendlyName && IsInitialized)
            {
                newDevice.Dispose();
                return true;
            }

            await InitializeDeviceInternalAsync(newDevice);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> InitializeDeviceAsync(string deviceId)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        
        try
        {
            var newDevice = _deviceEnumerator.GetDevice(deviceId);
            await InitializeDeviceInternalAsync(newDevice);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task InitializeDeviceInternalAsync(MMDevice newDevice)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        
        StopCapture();
        _selectedDevice = newDevice;
        _audioCapture = new WasapiLoopbackCapture(_selectedDevice)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                AudioConstants.DEFAULT_SAMPLE_RATE,
                AudioConstants.DEFAULT_CHANNELS
            )
        };

        _audioCapture.DataAvailable += (s, e) => DataAvailable?.Invoke(s, e);
        await Task.Run(() => _audioCapture.StartRecording());
    }

    public void StopCapture()
    {
        try
        {
            if (_audioCapture?.CaptureState == CaptureState.Capturing)
            {
                _audioCapture.StopRecording();
            }
            _audioCapture?.Dispose();
            _audioCapture = null;

            _selectedDevice?.Dispose();
            _selectedDevice = null;
        }
        catch (Exception)
        {
        }
    }

    public IEnumerable<MMDevice> GetAvailableDevices()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        return _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        StopCapture();
        _deviceEnumerator.Dispose();
        _isDisposed = true;
    }

    ~AudioDeviceManager() => Dispose();
} 