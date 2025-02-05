using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VRCOSC.Modules.OSCAudioReaction.AudioProcessing;

public class AudioDeviceManager : IAudioDeviceManager
{
    private readonly IAudioConfiguration _config;
    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _selectedDevice;
    private WasapiLoopbackCapture? _audioCapture;
    private bool _isDisposed;

    public bool IsInitialized => _audioCapture?.CaptureState == CaptureState.Capturing;
    public string? CurrentDeviceName => _selectedDevice?.FriendlyName;
    public WasapiLoopbackCapture? AudioCapture => _audioCapture;
    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public AudioDeviceManager(IAudioConfiguration config)
    {
        _config = config;
        _deviceEnumerator = new MMDeviceEnumerator();
    }

    public async Task<bool> InitializeDefaultDeviceAsync()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        
        try
        {
            _deviceEnumerator ??= new MMDeviceEnumerator();
            var newDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            if (_selectedDevice?.FriendlyName == newDevice.FriendlyName && 
                _audioCapture?.CaptureState == CaptureState.Capturing)
            {
                newDevice.Dispose();
                return true;
            }

            await InitializeDeviceInternalAsync(newDevice);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioDeviceManager] Initialize error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> InitializeDeviceAsync(string deviceId)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        
        try
        {
            _deviceEnumerator ??= new MMDeviceEnumerator();
            var newDevice = _deviceEnumerator.GetDevice(deviceId);
            await InitializeDeviceInternalAsync(newDevice);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioDeviceManager] Initialize device error: {ex.Message}");
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

        // Log device and format details
        System.Diagnostics.Debug.WriteLine($"[AudioDeviceManager] Device: {_selectedDevice.FriendlyName}");
        System.Diagnostics.Debug.WriteLine($"[AudioDeviceManager] Format: {_audioCapture.WaveFormat}");

        _audioCapture.DataAvailable += (s, e) => DataAvailable?.Invoke(s, e);
        await Task.Run(() => _audioCapture.StartRecording());
    }

    public void StopCapture()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        
        try
        {
            if (_audioCapture != null)
            {
                if (_audioCapture.CaptureState == CaptureState.Capturing)
                {
                    _audioCapture.StopRecording();
                }
                _audioCapture.Dispose();
                _audioCapture = null;
            }

            if (_selectedDevice != null)
            {
                _selectedDevice.Dispose();
                _selectedDevice = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioDeviceManager] Stop capture error: {ex.Message}");
        }
    }

    public IEnumerable<MMDevice> GetAvailableDevices()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(AudioDeviceManager));
        
        _deviceEnumerator ??= new MMDeviceEnumerator();
        return _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                StopCapture();
                if (_deviceEnumerator != null)
                {
                    _deviceEnumerator.Dispose();
                    _deviceEnumerator = null;
                }
            }
            _isDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~AudioDeviceManager()
    {
        Dispose(false);
    }
} 