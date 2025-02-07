# Audio Direction Module

A VRCOSC module that captures your system's audio output and sends stereo direction and volume information to VRChat parameters for audio visualization + outputs of the frequency bands.

Based on [VRC-OSC-Audio-Reaction](https://github.com/Codel1417/VRC-OSC-Audio-Reaction) by Codel1417.
Speacial thanks to [Wikipedia](https://en.wikipedia.org/wiki/Fast_Fourier_transform#Multidimensional_FFTs) as without them i would have no idea how to do this.
## Features

- **Audio Direction Detection**: Calculates the stereo balance of your audio output (0 = left, 0.5 = center, 1 = right)
  - Direction is weighted by the power of enabled frequency bands
  - Smoothly returns to center when audio is below threshold
  - Only considers enabled frequency bands in direction calculation
- **Volume Level Detection**: Measures the overall volume level (0 = silent, 1 = loud)
- **Volume Spike Detection**: Detects sudden increases in volume
  - Configurable sensitivity threshold
  - Note: Can be finnicky and may not detect all spikes consistently
  - Works best with clear, sharp volume changes
  - May need adjustment based on your audio source and preferences
- **Automatic Gain Control**: Dynamically adjusts gain to maintain consistent volume levels
  - Target level: 0.5
  - AGC speed: 0.1
  - Gain range: 0.1 to 5.0
- **Frequency Analysis**: 
  - Individual toggles for each frequency band
  - Each band provides independent intensity values (0-1)
  - Bands are normalized relative to total power of enabled bands
  - Disabled bands output 0
- **Avatar-Focused Presets**:
  - Default: Balanced settings (FFT: 8192, Smoothing: 0.5)
  - Low Latency: Quick response (FFT: 4096, Smoothing: 0.3)
  - Voice Optimized: Speech focused (FFT: 4096, Smoothing: 0.4)
  - High Smoothing: Stable reactions (FFT: 16384, Smoothing: 0.8)
  - Music Optimized: Balanced for music (FFT: 8192, Smoothing: 0.5)
- **Adaptive Processing**:
  - Automatic FFT size adjustment based on buffer size
  - Minimum FFT size: 4096
  - Maximum FFT size: Based on preset
  - Buffer underrun detection and logging

## Parameters

The following float parameters are available:

### Main Parameters
- `audio_direction`: Stereo balance (0 = left, 0.5 = center, 1 = right)
  - Weighted by power of enabled frequency bands
  - Smoothly drifts to center when signal is weak
  - Affected by direction threshold setting
- `audio_volume`: Overall volume level (0 = silent, 1 = loud)
  - RMS-based calculation
  - Affected by gain and AGC settings
  - Smoothed based on configuration
- `audio_spike`: Boolean that triggers on sudden volume increases
  - True when a significant volume jump is detected
  - Sensitivity can be adjusted in settings
  - Note: Detection can be inconsistent depending on audio characteristics

### Frequency Band Parameters
Each band outputs a normalized value (0-1) representing its relative power:

- `audio_subbass`: 20-60Hz (Sub Bass)
- `audio_bass`: 60-250Hz (Bass)
- `audio_lowmid`: 250-500Hz (Low Mids)
- `audio_mid`: 500Hz-2kHz (Mids)
- `audio_uppermid`: 2-4kHz (Upper Mids)
- `audio_presence`: 4-6kHz (Presence)
- `audio_brilliance`: 6-25kHz (Brilliance)

Note: 
- Disabled bands output 0
- Enabled bands are normalized relative to the total power of all enabled bands
- When "Scale Frequencies with Volume" is disabled, the sum of all enabled bands will equal 1.0
- Each band's power is calculated using proper frequency bin analysis and magnitude-squared values
- Bands use exponential smoothing for stable transitions

## Avatar Presets

Current preset configurations:

1. **Default**
   - FFT Size: 8192 (~5.86Hz resolution)
   - Gain: 1.0
   - Smoothing: 0.5
   - Direction Threshold: 0.01
   - Frequency Smoothing: 0.7
   - AGC: Enabled

2. **Low Latency**
   - FFT Size: 4096 (~11.7Hz resolution)
   - Gain: 1.2
   - Smoothing: 0.3
   - Direction Threshold: 0.01
   - Frequency Smoothing: 0.7
   - AGC: Enabled

3. **Voice Optimized**
   - FFT Size: 4096 (~11.7Hz resolution)
   - Gain: 1.5
   - Smoothing: 0.4
   - Direction Threshold: 0.02
   - Frequency Smoothing: 0.7
   - AGC: Enabled

4. **High Smoothing**
   - FFT Size: 16384 (~2.93Hz resolution)
   - Gain: 1.0
   - Smoothing: 0.8
   - Direction Threshold: 0.015
   - Frequency Smoothing: 0.7
   - AGC: Enabled

5. **Music Optimized**
   - FFT Size: 8192 (~5.86Hz resolution)
   - Gain: 1.1
   - Smoothing: 0.5
   - Direction Threshold: 0.01
   - Frequency Smoothing: 0.7
   - AGC: Enabled

Note: FFT resolution values indicate the frequency spacing between bins. Lower values mean better frequency resolution but higher latency.

## Technical Details

- Uses NAudio's `WasapiLoopbackCapture` for system audio capture
- 48kHz sample rate, 32-bit float stereo format
- Fixed FFT sizes based on preset (4096, 8192, or 16384 samples)
- Hamming window applied to audio samples
- Direction calculation:
  - Per-band direction weighted by band power
  - Only enabled bands contribute
  - Smooth center drift when signal is weak
- Frequency analysis:
  - Power spectrum calculation using magnitude-squared values
  - Proper frequency bin to band mapping
  - Independent band enabling/disabling
  - Optional volume scaling for band outputs
- Memory management:
  - Thread-safe FFT buffer handling
  - Zero-padding for partial buffers
  - Proper resource cleanup
  - Automatic error recovery

## Basic Settings

- **Gain**: 0.1 to 5.0 (default varies by preset)
- **AGC**: Automatic gain control (default: enabled)
- **Animation Smoothing**: 0 to 1 (default varies by preset)
- **Direction Threshold**: 0.005 to 0.1 (default varies by preset)
- **Spike Sensitivity**: 0.5 to 5.0 (default: 2.0)
  - Lower values make spike detection more sensitive
  - Higher values require larger volume jumps
  - May need experimentation to find the right value for your setup
- **Frequency Smoothing**: Independent smoothing for frequency analysis
- **FFT Size**: 4096, 8192, or 16384 (preset dependent)
- **Band Toggles**: Enable/disable specific frequency ranges

## Troubleshooting

- **No Audio Detection**:
  - Module will log buffer size changes
  - Checks for minimum valid signal (1e-6)
  - Automatic device recovery
- **Direction Issues**:
  - Checks for minimum channel sum (0.01)
  - Smooth center drift when below threshold
  - Only enabled bands affect direction
- **Volume Problems**:
  - AGC targets 0.5 level
  - Manual gain range: 0.1 to 5.0
  - RMS-based volume calculation
- **Spike Detection Issues**:
  - Try adjusting the Spike Sensitivity
  - Note that detection can be inconsistent
  - Works best with sharp volume changes
  - May miss some spikes or trigger unexpectedly
  - Consider your use case - lower threshold for subtle changes, higher for dramatic ones
- **Performance Issues**:
  - Adaptive FFT size
  - Buffer underrun detection
  - Efficient memory management

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- VRCOSC
- VRChat with OSC enabled

## Installation

1. Enable the module in VRCOSC
2. Select appropriate presets and frequency bands for your avatar
3. Add the parameters to your avatar

## Avatar Setup

1. Add the wanted float parameters to your avatar:
   - basic: `audio_direction` and `audio_volume`
   - Optional: Individual frequency band parameters
2. Choose appropriate preset based on your use case:
   - Default: Balanced settings suitable for most use cases
   - Voice Optimized: For speech and vocal pattern detection
   - Low Latency: For immediate response to audio
   - High Smoothing: For smooth, stable reactions
   - Music Optimized: For musical performance and visualization
3. Fine-tune settings if needed:
   - Adjust FFT size based on needed responsiveness
   - Enable only required frequency bands
   - Adjust smoothing for desired animation style

## Troubleshooting

- If no audio is detected:
  - Check that your default output device is working
  - Check the debug logs for any error messages
  - The module will automatically attempt to recover
- If direction seems incorrect:
  - Try adjusting the Direction Sensitivity
  - Make sure you have stereo audio playing
  - Minimum direction threshold is 0.005
- If volume is too low/high:
  - Enable Auto Gain for automatic adjustment
  - Or adjust Manual Gain (0.1 to 5.0 range)
  - AGC targets a level of 0.5
- If animations are too jittery:
  - Increase the Animation Smoothing value
  - Try a preset with higher smoothing values
  - Adjust Frequency Smoothing for band analysis
- If frequency analysis seems delayed:
  - Try the Low Latency preset
  - Or manually set a lower FFT size
  - Consider trade-off between quality and latency
- If the module stops working:
  - It will automatically attempt to recover
  - Check the debug logs for error messages 