using System.Collections.Concurrent;
using System.Numerics;
using NAudio.Wave;
using VRCOSC.App.SDK.Modules;

namespace VRCOSC.Modules.OSCAudioReaction.AudioProcessing;

public class AudioFactory : IAudioFactory
{

    public IAudioProcessor CreateProcessor(IAudioConfiguration config, int bytesPerSample)
    {
        if (bytesPerSample <= 0)
            throw new ArgumentException("Bytes per sample must be greater than 0", nameof(bytesPerSample));

        return new AudioProcessor(config, bytesPerSample);
    }

    public IAudioDeviceManager CreateDeviceManager(IAudioConfiguration config)
    {
        return new AudioDeviceManager(config);
    }
}

public class AudioProcessor : IAudioProcessor, IDisposable
{
    private readonly ConcurrentQueue<float> _volumeHistory;
    private readonly ConcurrentQueue<float> _directionHistory;
    private readonly IAudioConfiguration _config;
    private float _currentGain;
    private float _smoothedVolume;
    private float _smoothedDirection;
    private float _currentRms;
    private float[] _frequencyBands;
    private float[] _smoothedBands;
    private bool[] _enabledBands;
    private float[] _bandDirections;
    private int _bytesPerSample;
    private volatile bool _isActive;
    private int _sampleRate;
    private float[] _fftBuffer;
    private DateTime _lastProcessTime;
    private const float MIN_VALID_SIGNAL = 1e-6f;
    private int _lastBufferSize;
    private int _underrunCount;
    private int _adaptedFftSize;
    private bool _isDisposed;
    private readonly object _fftLock = new object();
    private float _lastAverageVolume = 0f;
    private const float MIN_SPIKE_VOLUME = 0.1f;
    private bool _currentSpike = false;
    private DateTime _lastSpikeTime = DateTime.MinValue;
    private const int MIN_SPIKE_INTERVAL_MS = 100;
    private const int SPIKE_DURATION_MS = 50;
    private const int VOLUME_HISTORY_SIZE = 3;
    private readonly Queue<float> _recentVolumes = new(VOLUME_HISTORY_SIZE);

    public float CurrentVolume => _smoothedVolume;
    public float CurrentDirection => _smoothedDirection;
    public float CurrentGain => _currentGain;
    public float CurrentRms => _currentRms;
    public float[] FrequencyBands => _smoothedBands;
    public bool IsActive => _isActive;
    public bool HasSpike => _currentSpike;

    public AudioProcessor(IAudioConfiguration config, int bytesPerSample)
    {
        _config = config;
        _bytesPerSample = bytesPerSample;
        _currentGain = config.Gain;
        _smoothedDirection = 0.5f;
        _smoothedVolume = 0f;
        _volumeHistory = new ConcurrentQueue<float>();
        _directionHistory = new ConcurrentQueue<float>();
        _sampleRate = AudioConstants.DEFAULT_SAMPLE_RATE;
        _frequencyBands = Array.Empty<float>();
        _smoothedBands = Array.Empty<float>();
        _enabledBands = Array.Empty<bool>();  // Initialize empty enabled bands array
        _bandDirections = Array.Empty<float>();  // Initialize empty band directions array
        _adaptedFftSize = config.FftSize;  // Start with configured size
        LogDebug($"Initializing AudioProcessor with configured FFT size: {config.FftSize}, bytes per sample: {bytesPerSample}");
        _fftBuffer = new float[_adaptedFftSize];
        _lastProcessTime = DateTime.Now;
        ConfigureFrequencyBands(config.FrequencySmoothing, new bool[AudioConstants.DEFAULT_FREQUENCY_BANDS]);
        Reset();
    }

    public void Reset()
    {
        if (_isDisposed) return;

        _volumeHistory.Clear();
        _directionHistory.Clear();
        for (int i = 0; i < AudioConstants.HISTORY_SIZE; i++)
        {
            _volumeHistory.Enqueue(0f);
            _directionHistory.Enqueue(0.5f);
        }
        _currentRms = 0f;
        _smoothedVolume = 0f;
        _lastAverageVolume = 0f;
        _currentSpike = false;
        _recentVolumes.Clear();
        for (int i = 0; i < VOLUME_HISTORY_SIZE; i++)
        {
            _recentVolumes.Enqueue(0f);
        }
        // Don't reset direction immediately, let it drift
        lock (_fftLock)
        {
            Array.Clear(_frequencyBands, 0, _frequencyBands.Length);
            Array.Clear(_smoothedBands, 0, _smoothedBands.Length);
            Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
        }
        _isActive = true;
        _lastProcessTime = DateTime.Now;
    }

    private int FindNearestPowerOfTwo(int value)
    {
        int power = (int)Math.Floor(Math.Log2(value));
        int lowerPower = 1 << power;
        int upperPower = 1 << (power + 1);
        
        return (value - lowerPower) < (upperPower - value) ? lowerPower : upperPower;
    }

    private void AdaptFftSize(int samplesAvailable)
    {
        // Only adapt if we need to
        if (samplesAvailable >= _adaptedFftSize) return;

        int newSize = FindNearestPowerOfTwo(samplesAvailable);
        
        // Don't go below minimum FFT size
        newSize = Math.Max(newSize, AudioConstants.FFT_SIZE_LOW);
        
        // Don't exceed configured FFT size
        newSize = Math.Min(newSize, _config.FftSize);

        if (newSize != _adaptedFftSize)
        {
            LogDebug($"Adapting FFT size: {_adaptedFftSize} -> {newSize} (samples available: {samplesAvailable})");
            lock (_fftLock)
            {
                var oldBuffer = _fftBuffer;
                _fftBuffer = new float[newSize];
                _adaptedFftSize = newSize;
                Array.Clear(oldBuffer, 0, oldBuffer.Length);
            }
        }
    }

    public void UpdateEnabledBands(bool[] enabledBands)
    {
        if (enabledBands.Length != _frequencyBands.Length)
        {
            LogDebug($"Invalid enabled bands array length: {enabledBands.Length} != {_frequencyBands.Length}");
            return;
        }
        _enabledBands = (bool[])enabledBands.Clone();
    }

    public void UpdateSmoothing(float smoothing)
    {
        _config.Smoothing = Math.Clamp(smoothing, 0f, 1f);
    }

    public void UpdateGain(float gain)
    {
        _config.Gain = Math.Clamp(gain, AudioConstants.MIN_GAIN, AudioConstants.MAX_GAIN);
        if (!_config.EnableAGC)
        {
            _currentGain = _config.Gain;
        }
    }

    public void ConfigureFrequencyBands(float smoothing, bool[] enabledBands)
    {
        _config.FrequencySmoothing = Math.Clamp(smoothing, 0f, 1f);
        if (enabledBands == null || enabledBands.Length < 1) return;
        
        int numBands = enabledBands.Length;
        _frequencyBands = new float[numBands];
        _smoothedBands = new float[numBands];
        _enabledBands = (bool[])enabledBands.Clone();
        _bandDirections = new float[numBands];
        Array.Fill(_bandDirections, 0.5f);  // Default to center
    }

    public float GetFrequencyBand(int band)
    {
        if (band < 0 || band >= _frequencyBands.Length)
            return 0f;
        return _frequencyBands[band];
    }

    private static (float[] bands, float volume, float direction) AnalyzeFrequencyBands(ReadOnlySpan<float> samples, int numBands, int sampleRate, bool scaleWithVolume, bool[] enabledBands)
    {
        if (samples.IsEmpty || !IsPowerOfTwo(samples.Length))
            return (new float[numBands], 0f, 0.5f);

        int samplesPerChannel = samples.Length / 2;
        float binSize = sampleRate / (float)samplesPerChannel;
        int nyquistLimit = samplesPerChannel / 2;

        // Calculate mono samples and apply window
        var monoSamples = new float[samplesPerChannel];
        for (int i = 0; i < samplesPerChannel; i++)
        {
            monoSamples[i] = (samples[i * 2] + samples[i * 2 + 1]) / 2f;
        }

        // Calculate volume
        float volume = 0;
        for (int i = 0; i < samplesPerChannel; i++)
        {
            volume += monoSamples[i] * monoSamples[i];
        }
        volume = MathF.Sqrt(volume / samplesPerChannel);
        volume = Math.Min(volume, 1.0f);

        // Apply window function
        for (int i = 0; i < samplesPerChannel; i++)
        {
            monoSamples[i] *= HannWindow(i, samplesPerChannel);
        }

        // Perform FFT
        var complexData = new Complex[samplesPerChannel];
        for (int i = 0; i < samplesPerChannel; i++)
        {
            complexData[i] = new Complex(monoSamples[i], 0);
        }
        FFT(complexData);

        // Calculate power spectrum
        var powerSpectrum = new double[nyquistLimit];
        for (int bin = 0; bin < nyquistLimit; bin++)
        {
            double magnitude = Complex.Abs(complexData[bin]) / samplesPerChannel;
            powerSpectrum[bin] = magnitude * magnitude;
        }

        // Calculate bands and their individual directions
        var bands = new float[numBands];
        var bandDirections = new float[numBands];
        var bandWeights = new float[numBands];

        for (int band = 0; band < numBands && band < AudioConstants.FrequencyBands.Length; band++)
        {
            if (!enabledBands[band]) continue;

            var (lowFreq, highFreq, _, _) = AudioConstants.FrequencyBands[band];
            int lowBin = Math.Max(0, Math.Min((int)(lowFreq / binSize), nyquistLimit));
            int highBin = Math.Max(0, Math.Min((int)(highFreq / binSize), nyquistLimit));

            // Calculate band power and direction
            float leftSum = 0f, rightSum = 0f;
            double bandPower = 0;

            for (int bin = lowBin; bin < highBin; bin++)
            {
                bandPower += powerSpectrum[bin];
                int sampleIndex = bin * 2;
                if (sampleIndex < samples.Length - 1)
                {
                    leftSum += Math.Abs(samples[sampleIndex]);
                    rightSum += Math.Abs(samples[sampleIndex + 1]);
                }
            }

            bands[band] = (float)bandPower;
            float totalChannelSum = leftSum + rightSum;
            bandDirections[band] = totalChannelSum > 0.01f ? rightSum / totalChannelSum : 0.5f;
            bandWeights[band] = (float)bandPower;  // Use band power as weight
        }

        // Calculate weighted average direction from enabled bands
        float totalWeight = 0f;
        float weightedDirection = 0f;
        for (int i = 0; i < numBands; i++)
        {
            if (enabledBands[i] && bandWeights[i] > MIN_VALID_SIGNAL)
            {
                weightedDirection += bandDirections[i] * bandWeights[i];
                totalWeight += bandWeights[i];
            }
        }

        float direction = totalWeight > MIN_VALID_SIGNAL ? weightedDirection / totalWeight : 0.5f;

        // Normalize band powers
        float totalPower = 0f;
        for (int i = 0; i < numBands; i++)
        {
            if (enabledBands[i])
            {
                totalPower += bands[i];
            }
        }

        if (totalPower > 0f)
        {
            for (int i = 0; i < numBands; i++)
            {
                if (enabledBands[i])
                {
                    bands[i] /= totalPower;
                }
                else
                {
                    bands[i] = 0f;
                }
            }
        }
        else
        {
            Array.Clear(bands, 0, bands.Length);
        }

        return (bands, volume, direction);
    }

    public (float[] bands, float volume, float direction, bool spike) ProcessAudioData(WaveInEventArgs e, bool scaleWithVolume)
    {
        if (!_isActive)
            return (Array.Empty<float>(), 0f, 0.5f, false);

        var now = DateTime.Now;

        // Only reset spike if enough time has passed since last spike
        if ((now - _lastSpikeTime).TotalMilliseconds > SPIKE_DURATION_MS)
        {
            _currentSpike = false;
        }

        if ((now - _lastProcessTime).TotalSeconds > 1)
        {
            LogDebug("Audio timeout detected, resetting processor");
            // Apply center drift before reset
            float driftSpeed = 0.1f; // Faster drift for timeout
            _smoothedDirection = _smoothedDirection + (0.5f - _smoothedDirection) * driftSpeed;
            Reset();
            _lastProcessTime = now;
            return (_smoothedBands, _smoothedVolume, _smoothedDirection, _currentSpike);
        }

        var samples = ConvertToFloatSamples(e);
        if (samples.Length < _adaptedFftSize)
            return (_smoothedBands, _smoothedVolume, _smoothedDirection, _currentSpike);

        // Check for valid audio signal
        bool hasSignal = false;
        float maxSample = 0f;
        for (int i = 0; i < Math.Min(samples.Length, 100); i++)
        {
            float abs = Math.Abs(samples[i]);
            maxSample = Math.Max(maxSample, abs);
            if (abs > MIN_VALID_SIGNAL)
            {
                hasSignal = true;
                break;
            }
        }

        if (!hasSignal)
        {
            // Drift towards center when no signal - about 1 second to center
            float driftSpeed = 0.1f;  // 10% per frame ≈ 1 second to center
            _smoothedDirection = _smoothedDirection + (0.5f - _smoothedDirection) * driftSpeed;
            // Also smoothly reduce volume
            _smoothedVolume = _smoothedVolume * 0.9f; // Faster volume reduction too
            return (_smoothedBands, _smoothedVolume, _smoothedDirection, _currentSpike);
        }

        samples.Slice(0, _adaptedFftSize).CopyTo(_fftBuffer);
        var result = AnalyzeFrequencyBands(_fftBuffer, _frequencyBands.Length, _sampleRate, false, _enabledBands);
        
        if (result.volume < MIN_VALID_SIGNAL)
        {
            // Drift towards center when volume too low
            float driftSpeed = 0.1f;
            _smoothedDirection = _smoothedDirection + (0.5f - _smoothedDirection) * driftSpeed;
            return (_smoothedBands, _smoothedVolume, _smoothedDirection, _currentSpike);
        }

        float rawRms = result.volume;  // Get raw RMS before any processing

        // Now continue with normal audio processing
        _frequencyBands = result.bands;
        _currentRms = result.volume;

        // AGC processing
        if (_config.EnableAGC)
        {
            float error = AudioConstants.TARGET_LEVEL - _currentRms;
            float gainAdjustment = error * AudioConstants.AGC_SPEED;
            float newGain = Math.Clamp(_currentGain + gainAdjustment, AudioConstants.MIN_GAIN, AudioConstants.MAX_GAIN);
            
            const float MAX_GAIN_CHANGE = 0.1f;
            float gainDelta = Math.Clamp(newGain - _currentGain, -MAX_GAIN_CHANGE, MAX_GAIN_CHANGE);
            _currentGain += gainDelta;
        }
        else
        {
            _currentGain = _config.Gain;  // Use manual gain when AGC is disabled
        }

        // Apply gain to RMS
        _currentRms *= _currentGain;
        _currentRms = Math.Min(_currentRms, 1.0f);
        
        // Update smoothed volume with less aggressive smoothing for better responsiveness
        float volumeSmoothingFactor = Math.Min(_config.Smoothing, 0.5f);  // Cap volume smoothing at 0.5
        float newVolume = _currentRms * (1 - volumeSmoothingFactor) + _smoothedVolume * volumeSmoothingFactor;
        if (!float.IsNaN(newVolume) && !float.IsInfinity(newVolume))
            _smoothedVolume = newVolume;

        // Update direction with center drift when below threshold
        if (_currentRms >= _config.DirectionThreshold)
        {
            float newDirection = result.direction * (1 - _config.Smoothing) + _smoothedDirection * _config.Smoothing;
            if (!float.IsNaN(newDirection) && !float.IsInfinity(newDirection))
                _smoothedDirection = newDirection;
        }
        else
        {
            // Drift towards center when below threshold - use faster drift
            float driftSpeed = 0.1f;  // Fixed faster drift speed
            _smoothedDirection = _smoothedDirection + (0.5f - _smoothedDirection) * driftSpeed;
        }

        // Update and normalize frequency bands
        double totalBandPower = 0;
        for (int i = 0; i < _frequencyBands.Length; i++)
        {
            float newBandValue = _frequencyBands[i] * (1 - _config.FrequencySmoothing) + 
                               _smoothedBands[i] * _config.FrequencySmoothing;
            
            if (!float.IsNaN(newBandValue) && !float.IsInfinity(newBandValue))
            {
                _smoothedBands[i] = newBandValue;
                totalBandPower += newBandValue;
            }
        }

        // Normalize and scale bands
        if (totalBandPower > 0)
        {
            for (int i = 0; i < _smoothedBands.Length; i++)
            {
                _smoothedBands[i] = (float)(_smoothedBands[i] / totalBandPower);
                if (scaleWithVolume)
                    _smoothedBands[i] *= _smoothedVolume;
            }
        }

        // Detect volume spikes using rolling average
        if (_smoothedVolume >= MIN_SPIKE_VOLUME && !_currentSpike)
        {
            float averageVolume = UpdateVolumeHistory(_smoothedVolume);
            float relativeIncrease = _lastAverageVolume > 0 ? (averageVolume - _lastAverageVolume) / _lastAverageVolume : 0;
            bool timeOk = (now - _lastSpikeTime).TotalMilliseconds >= MIN_SPIKE_INTERVAL_MS;

            if (relativeIncrease > _config.SpikeThreshold && timeOk)
            {
                _currentSpike = true;
                _lastSpikeTime = now;
                LogDebug($"Spike: {relativeIncrease:P0}");
            }
            _lastAverageVolume = averageVolume;
        }
        else if (_smoothedVolume < MIN_SPIKE_VOLUME)
        {
            UpdateVolumeHistory(_smoothedVolume);
            _lastAverageVolume = _recentVolumes.Average();
        }

        _lastProcessTime = now;
        return (_smoothedBands, _smoothedVolume, _smoothedDirection, _currentSpike);
    }

    private float UpdateVolumeHistory(float newVolume)
    {
        if (_recentVolumes.Count >= VOLUME_HISTORY_SIZE)
        {
            _recentVolumes.Dequeue();
        }
        _recentVolumes.Enqueue(newVolume);
        
        return _recentVolumes.Average();
    }

    private ReadOnlySpan<float> ConvertToFloatSamples(WaveInEventArgs e)
    {
        if (_bytesPerSample != 4)
        {
            LogDebug($"Invalid bytes per sample: {_bytesPerSample}");
            return ReadOnlySpan<float>.Empty;
        }

        var samplesPerChannel = e.BytesRecorded / (_bytesPerSample * 2);
        
        // Log buffer size changes
        if (_lastBufferSize != e.BytesRecorded)
        {
            _lastBufferSize = e.BytesRecorded;
            LogDebug($"Audio buffer size changed: Bytes={e.BytesRecorded}, Samples per channel={samplesPerChannel}, Current FFT size={_adaptedFftSize}");
            AdaptFftSize(samplesPerChannel * 2);
        }

        if (samplesPerChannel <= 0)
        {
            LogDebug($"Invalid samples per channel: Bytes={e.BytesRecorded}, BPS={_bytesPerSample}");
            return ReadOnlySpan<float>.Empty;
        }

        // Check for consistent buffer underruns
        if (samplesPerChannel * 2 < _adaptedFftSize)
        {
            _underrunCount++;
            if (_underrunCount >= 10)
            {
                LogDebug($"Consistent buffer underruns: Buffer samples={samplesPerChannel * 2}, Required FFT size={_adaptedFftSize}");
                _underrunCount = 0;
            }
        }
        else
        {
            _underrunCount = 0;
        }

        var samples = new float[samplesPerChannel * 2];
        var buffer = e.Buffer.AsSpan(0, e.BytesRecorded);

        for (int i = 0; i < samplesPerChannel * 2; i++)
        {
            var byteIndex = i * _bytesPerSample;
            samples[i] = BitConverter.ToSingle(buffer.Slice(byteIndex, _bytesPerSample));
        }

        return samples;
    }

    private static float HannWindow(int index, int size)
    {
        return 0.5f * (1 - MathF.Cos(2 * MathF.PI * index / (size - 1)));
    }

    private static bool IsPowerOfTwo(int x)
    {
        return (x != 0) && ((x & (x - 1)) == 0);
    }

    private static void FFT(Complex[] buffer)
    {
        int bits = (int)MathF.Log2(buffer.Length);
        
        for (int j = 1; j < buffer.Length; j++)
        {
            int swapPos = BitReverse(j, bits);
            if (swapPos > j)
            {
                (buffer[j], buffer[swapPos]) = (buffer[swapPos], buffer[j]);
            }
        }

        for (int N = 2; N <= buffer.Length; N <<= 1)
        {
            for (int i = 0; i < buffer.Length; i += N)
            {
                for (int k = 0; k < N / 2; k++)
                {
                    int evenIndex = i + k;
                    int oddIndex = i + k + (N / 2);
                    Complex even = buffer[evenIndex];
                    Complex odd = buffer[oddIndex];

                    double term = -2 * Math.PI * k / N;
                    Complex exp = new Complex(Math.Cos(term), Math.Sin(term)) * odd;

                    buffer[evenIndex] = even + exp;
                    buffer[oddIndex] = even - exp;
                }
            }
        }
    }

    private static int BitReverse(int n, int bits)
    {
        int reversed = 0;
        for (int i = 0; i < bits; i++)
        {
            reversed = (reversed << 1) | (n & 1);
            n >>= 1;
        }
        return reversed;
    }

    public void LogDebug(string message)
    {
        // Implementation of LogDebug method
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        _isActive = false;

        lock (_fftLock)
        {
            if (_fftBuffer != null)
            {
                Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
                _fftBuffer = Array.Empty<float>();
            }

            if (_frequencyBands != null)
            {
                Array.Clear(_frequencyBands, 0, _frequencyBands.Length);
                _frequencyBands = Array.Empty<float>();
            }

            if (_smoothedBands != null)
            {
                Array.Clear(_smoothedBands, 0, _smoothedBands.Length);
                _smoothedBands = Array.Empty<float>();
            }

            if (_bandDirections != null)
            {
                Array.Clear(_bandDirections, 0, _bandDirections.Length);
                _bandDirections = Array.Empty<float>();
            }

            if (_enabledBands != null)
            {
                Array.Clear(_enabledBands, 0, _enabledBands.Length);
                _enabledBands = Array.Empty<bool>();
            }
        }

        _volumeHistory.Clear();
        _directionHistory.Clear();
        GC.SuppressFinalize(this);
    }
} 