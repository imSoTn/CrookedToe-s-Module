# Audio Direction Module

A VRCOSC module that captures your system's audio output and sends stereo direction and volume information to VRChat parameters for audio visualization.

Based on [VRC-OSC-Audio-Reaction](https://github.com/Codel1417/VRC-OSC-Audio-Reaction) by Codel1417.

## Features

- **Audio Direction Detection**: Calculates the stereo balance of your audio output (0 = left, 0.5 = center, 1 = right)
- **Volume Level Detection**: Measures the overall volume level (0 = silent, 1 = loud)
- **Automatic Gain Control**: Dynamically adjusts gain to maintain consistent volume levels
- **Smoothing**: Configurable smoothing for both direction and volume changes
- **Rate Limiting**: Prevents parameter spam by limiting update frequency
- **Error Recovery**: Automatically recovers from audio device changes or errors

## Parameters

Two float parameters are available:

- `audio_direction` at `/avatar/parameters/audio_direction`: The direction of the sound. Where 0.5 is centered, 0 is left, 1 is right.
- `audio_volume` at `/avatar/parameters/audio_volume`: The volume of the sound. Where 0 is silent, 1 is loud. This is based on the Windows Audio API.

## Settings

- **Manual Gain**: Gain multiplier when AGC is disabled (0.1 to 5.0, default: 1.0)
- **Auto Gain**: Enable/disable automatic gain control (default: enabled)
- **Smoothing**: Smoothing factor for volume and direction changes (0 = none, 1 = max, default: 0.5)
- **Direction Threshold**: Minimum volume level required to calculate direction (0.001 to 0.1, default: 0.01)

## Technical Details

- Uses NAudio's `WasapiLoopbackCapture` to capture system audio output
- Samples at 48kHz with 32-bit float stereo format
- Updates at 30Hz (33ms intervals)
- Implements parameter rate limiting (minimum 50ms between updates)
- Values are clamped to avoid VRChat parameter bugs (minimum 0.005)

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- VRCOSC
- VRChat with OSC enabled

## Installation

1. Place the module in your VRCOSC modules folder
2. Enable the module in VRCOSC
3. Configure the settings as needed
4. Add the parameters to your avatar

## Avatar Setup

1. Add two float parameters to your avatar:
   - `audio_direction` (0-1 range)
   - `audio_volume` (0-1 range)
2. Use these parameters in your animations/effects for audio visualization

## Troubleshooting

- If no audio is detected, check that your default output device is working
- If direction seems incorrect, try adjusting the Direction Threshold
- If volume is too low/high, adjust Manual Gain or enable Auto Gain
- If updates are too jittery, increase the Smoothing value 