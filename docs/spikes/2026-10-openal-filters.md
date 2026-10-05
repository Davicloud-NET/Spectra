# OpenAL filters and the loopback device from NativeAOT, October 2026

Verdict: works, with a catch. The low-pass filter and the loopback device of the packaged OpenAL Soft 1.23.1 can both be driven through function pointers. All 27 checks pass on Windows and on Linux, under the JIT and as a NativeAOT executable, and all twelve runs print the same numbers and the same hashes of the rendered sound.
The catch is how a change arrives. OpenAL Soft switches to a new gain HF in one step, and the step also knocks the low end for a few samples: 22% of a 200 Hz tone's amplitude for a move from 1 to 0.85. The steps have to be kept small on our side.
Two more results shape the build. Gain HF is the level at 5 kHz, and the top of the band falls to its square. One change with its attach costs about 180 ns, so 32 a frame is 6 us.

This is a spike. It produces answers, not the feature. Everything lives under
`spikes/openal-filters/`, nothing in the engine changed, and the project is not
in `Spectra.slnx`. Nothing was played: every sound was rendered into memory and
no real device was opened.

## Rig and versions

| | |
| --- | --- |
| Machine | 13th Gen Intel Core i9-13900K, 32 logical CPUs, 32 GB |
| Windows | Windows 11 Pro 10.0.26300 |
| Linux | Ubuntu 24.04.3 LTS in WSL2, kernel 6.6.87.2-microsoft-standard-WSL2 |
| OpenAL Soft | 1.23.1, the files in `Silk.NET.OpenAL.Soft.Native` 1.23.1: `soft_oal.dll`, 1,113,088 B, and `libopenal.so`, 1,159,736 B. `AL_VERSION` reads `1.1 ALSOFT 1.23.1`. |
| Binding | `Silk.NET.OpenAL` 2.23.0. Both versions come from `Directory.Packages.props`, as the engine's do. |
| .NET | SDK 10.0.401, runtime and ILCompiler 10.0.12, on both systems |
| Linux linker | clang 18.1.3 |

The library is loaded with `GetApi(soft: true)`, as `OpenAlBackend` does.

The machine was in use while the spike ran. Timings are the best of several
rounds, not pinned to a core. Between runs of one build they moved by up to
5 percent, and the smallest one, `alFilterf`, by 9.

There is no `alsoft.ini` on the Windows side. WSL was not checked for one.
Nothing was downloaded or installed. The header and source lines cited below
were read on GitHub, in `kcat/openal-soft` at tag `1.23.1`, as excerpts and
not from a checkout. Each one is there to explain a measurement. The one that
stands alone, the mixer period in section 4, is marked.

## What was built

`OpenAlFilterSpike/` is a C# console project, about 1,500 lines. `Efx.cs` and
`Loopback.cs` hold the function pointers and the constants. `Rig.cs` opens a
mono loopback device at 48 kHz. `Checks/` has one file per question. The
program prints PASS or FAIL with numbers, and it exits with 1 on a failure.

The test signal is a 16 bit mono buffer at 48 kHz, as the engine uploads PCM.
A level is read from a window of 4,800 samples by summing against a sine and a
cosine of the frequency. Every tone is a multiple of 10 Hz, so the window holds
whole cycles and nothing leaks between tones.

## Reproduce

From the repository root, in an ordinary shell.

```powershell
spikes/openal-filters/run-windows.ps1   # JIT build, NativeAOT publish, three runs of each, compared
spikes/openal-filters/run-linux.ps1     # the same in WSL, compared with the Windows runs
```

Logs land in `spikes/openal-filters/out/<rid>/logs/`. `out/` is git-ignored.
Lines that start with `INFO` or `TIME` name the machine and the timings. The
scripts compare every other line.

## 1. Filters through function pointers

`ALC_EXT_EFX` is present on the device, version 1.0. Seven entry points were
asked for with `alGetProcAddress`. All seven came back, and each is the same
address as the library's export of that name.

The engine needs four of them, and one core call:

| Entry point | C# type | Job |
| --- | --- | --- |
| `alGenFilters` | `delegate* unmanaged[Cdecl]<int, uint*, void>` | make the one filter object |
| `alDeleteFilters` | `delegate* unmanaged[Cdecl]<int, uint*, void>` | free it |
| `alFilteri` | `delegate* unmanaged[Cdecl]<uint, int, int, void>` | make it a low-pass, once |
| `alFilterf` | `delegate* unmanaged[Cdecl]<uint, int, float, void>` | set gain HF |
| `alSourcei` | core AL, already in Silk.NET | attach the filter to a source |

`alSourcei(source, AL_DIRECT_FILTER, filter)` worked through a fetched pointer
and through Silk.NET's typed call with a cast enum:
`al.SetSourceProperty(source, (SourceInteger)0x20005, (int)filter)`.

The constants, all from `include/AL/efx.h`:

| Constant | Value |
| --- | --- |
| `ALC_EXT_EFX_NAME` | `"ALC_EXT_EFX"` |
| `AL_DIRECT_FILTER` | `0x20005` |
| `AL_FILTER_TYPE` | `0x8001` |
| `AL_FILTER_NULL` | `0x0000` |
| `AL_FILTER_LOWPASS` | `0x0001` |
| `AL_LOWPASS_GAIN` | `0x0001` |
| `AL_LOWPASS_GAINHF` | `0x0002` |

The library agrees: `alGetEnumValue` returned the same number for each of the
six AL names. The header gives both gains the range 0 to 1 and the default 1.

What the calls do at the edges:

- A new filter has the type `AL_FILTER_NULL`. `alFilterf` on it raises
  `AL_INVALID_ENUM`. The type has to be set first.
- Setting the type again puts both gains back to 1. A gain HF of 0.25 read
  back as 1 afterwards.
- 1.5, -0.1 and NaN raise `AL_INVALID_VALUE` and leave the old value. 0 is
  accepted.
- Attaching a filter id that does not exist raises `AL_INVALID_VALUE`.
- `alGetSourcei(source, AL_DIRECT_FILTER)` raises `AL_INVALID_ENUM`, and the
  library prints `[ALSOFT] (EE) Unexpected property: 0x20005` on stderr. A
  source cannot be asked which filter it has.
- `alGetProcAddress` answers with no context current too. OpenAL Soft does
  that. The specification does not promise it.

## 2. The loopback device through function pointers

`ALC_SOFT_loopback` is present, asked with a null device. The three entry
points come from `alcGetProcAddress(null, name)`, before any device is open:

| Entry point | C# type |
| --- | --- |
| `alcLoopbackOpenDeviceSOFT` | `delegate* unmanaged[Cdecl]<byte*, Device*>` |
| `alcIsRenderFormatSupportedSOFT` | `delegate* unmanaged[Cdecl]<Device*, int, int, int, byte>` |
| `alcRenderSamplesSOFT` | `delegate* unmanaged[Cdecl]<Device*, void*, int, void>` |

The constants are in `include/AL/alext.h`, except `ALC_FREQUENCY`, which is
core ALC from `include/AL/alc.h`. `alcGetEnumValue` returned the same number
for all nine.

| Constant | Value |
| --- | --- |
| `ALC_FREQUENCY` | `0x1007` |
| `ALC_FORMAT_CHANNELS_SOFT` | `0x1990` |
| `ALC_FORMAT_TYPE_SOFT` | `0x1991` |
| `ALC_SHORT_SOFT` | `0x1402` |
| `ALC_FLOAT_SOFT` | `0x1406` |
| `ALC_MONO_SOFT` | `0x1500` |
| `ALC_STEREO_SOFT` | `0x1501` |
| `ALC_HRTF_SOFT` | `0x1992` |
| `ALC_OUTPUT_LIMITER_SOFT` | `0x199A` |

The device opens with a null name and calls itself "OpenAL Soft". It reports
48 kHz mono float, mono 16 bit, stereo float and 44.1 kHz as supported, and a
made-up channel layout as not. The context is created with the format in its
attribute list and reads the same values back.

What a context on it renders:

- With no source every sample is 0.
- A 1 kHz tone at amplitude 0.5 comes out at 0.49999, 0.000 dB from the
  source, in float and in 16 bit.
- The first sample of the first call is the first sample of the buffer. There
  is no delay.
- A source only moves when samples are rendered. After 2,400 samples of a
  4,800 sample sound it is Playing at offset 2,400. After 4,800 more it is
  Stopped. No wall clock is involved.
- 16 bit output is not the float output rounded. The tone peaks at 16,384 in
  the buffer and at 16,385 in the output. `alc/alc.cpp` turns dither on for
  16 bit output by default and seeds it with a constant.
- A 16 bit context has the output limiter on unless told otherwise, and a
  float context has it off. With the limiter on, all 48,000 samples of a
  second differed from the same render with it off.

The output is the same bytes from run to run. One second of the filtered test
signal hashes to `CE6293D033C4A320` in float and `A4070D6151F06DD0` in 16 bit
with the limiter off, the first 16 digits of the SHA-256. That held for:

- two renders in one process, each on a new device
- one call of 48,000 samples, calls of 800, and uneven calls of 1,024, 333,
  4,096 and 17
- three processes each of the JIT build and the NativeAOT build, on Windows
  and on Linux: twelve runs

## 3. What the filter does

One buffer holds 14 tones at once, each at amplitude 0.05. The table is the
level of a tone against the same render with no filter, in dB. All twelve runs
print these numbers.

| Setting | 200 Hz | 500 Hz | 1 kHz | 2 kHz | 5 kHz | 8 kHz | 12 kHz | 20 kHz |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| gain HF 1 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 |
| gain HF 0.5 | 0.00 | 0.00 | -0.02 | -0.35 | -6.02 | -10.47 | -11.83 | -12.04 |
| gain HF 0.25 | 0.00 | -0.01 | -0.10 | -1.34 | -12.04 | -19.47 | -23.25 | -24.08 |
| gain HF 0.1 | 0.00 | -0.04 | -0.57 | -5.13 | -20.00 | -28.88 | -36.33 | -39.97 |
| gain HF 0.01 | -0.10 | -2.71 | -11.73 | -23.57 | -40.00 | -49.22 | -58.74 | -77.74 |
| gain HF 0.001 | -5.07 | -19.42 | -31.43 | -43.55 | -60.00 | -69.23 | -78.77 | -101.59 |
| gain HF 0 | -5.07 | -19.42 | -31.43 | -43.55 | -60.00 | -69.23 | -78.77 | -101.59 |
| gain 0.5 | -6.02 | -6.02 | -6.02 | -6.02 | -6.02 | -6.02 | -6.02 | -6.02 |
| gain 0.5, gain HF 0.1 | -6.02 | -6.06 | -6.59 | -11.16 | -26.02 | -34.90 | -42.35 | -45.99 |

With no filter the 200 Hz tone is at 0.000 dB from the source. A filter with
both gains at 1 renders the same bytes as no filter.

How to read it:

- "High" is 5 kHz. The level at 5 kHz is gain HF itself: -6.02 dB at 0.5,
  -20.00 dB at 0.1.
- Above 5 kHz it keeps falling, to the square of gain HF at the top of the
  band: -12.04 dB at 0.5, -39.97 dB at 0.1.
- It is a shelf, not a cutoff. The octave from 4 to 8 kHz falls 6.8 dB at
  0.5, 11.0 at 0.25, 12.9 at 0.1 and 13.3 at 0.01.
- A deeper setting reaches further down. At 0.01 a 1 kHz tone is already
  11.7 dB down and a 500 Hz tone 2.7 dB.
- Gain HF 0 is not silence above 5 kHz. It measures the same as 0.001.
- `AL_LOWPASS_GAIN` is a plain gain on every frequency, and the two multiply.

The spike compares every cell with a model: the high shelf of the Audio EQ
Cookbook, second order, slope 1, at 5 kHz, with the cookbook's `A` set to gain
HF. The worst difference over 9 settings and 14 tones is 0.002 dB. The source
says the same: `alc/alu.cpp` calls `setParamsFromSlope(BiquadType::HighShelf,
hfNorm, DryGain.HF, 1.0f)`, `core/filters/biquad.h` limits the gain to 0.001
with the comment "Limit -60dB", and `al/filter.h` sets the reference to
5000 Hz. EFX has no parameter that moves the reference for this filter.

## 4. Changing it while a sound plays

A change to the filter object does not reach a source that has it attached. An
8 kHz tone stayed at 0.000 dB after `alFilterf` alone, and fell to -28.88 dB
once the filter was attached again. `al/source.cpp` copies the gains into the
source in its `AL_DIRECT_FILTER` case. So every change is `alFilterf` and then
`alSourcei`.

The change is in the output from the first sample of the next render call. On
a real device the mixer runs on its own thread once per period.
`core/device.h` sets the default period to 960 samples, 20 ms. That was read,
not measured.

How it arrives depends on what changed. An 8 kHz tone, its level read from a
window of one period:

| Change | Leaves the old level at sample | At the new level by sample |
| --- | --- | --- |
| `AL_GAIN` on the source, 1 to 0.1 | 2 | 59 |
| `AL_LOWPASS_GAIN`, 1 to 0.1 | 2 | 59 |
| `AL_LOWPASS_GAINHF`, 1 to 0.1 | 0 | 0 |
| first attach, gain HF 0.1 | 0 | 0 |
| detach from gain HF 0.1 | 0 | 0 |

"Leaves" and "at" mean within 5% of the step. The numbers are the same for
render calls of 64, 256, 1,024 and 4,800 samples.

So a plain gain is ramped over 64 samples, 1.3 ms. `core/voice.cpp` has the
64. Gain HF is not ramped. The 8 kHz level is at -24.5 dB in the first window,
moves between -25.8 and -32.2 dB for 12 samples, and is within 0.3 dB of its
final -28.9 dB from sample 16 on.

### The step reaches the low end

A 200 Hz tone passes the filter within 0.1 dB at every setting from 1 down to
0.01. A change of gain HF still moves it. The table is the biggest move
between two neighbouring samples in the 64 samples after a change, as a share
of the tone's amplitude. The change was made while the tone sat on its peak,
which is the worst moment.

| Gain HF | Biggest move |
| --- | --- |
| no change, the tone alone | 2.6% |
| none to 0.85 | 22.3% |
| 0.9999 to 0.85 | 22.3% |
| none to 0.5 | 65.8% |
| none to 0.01 | 99.8% |
| 0.5 to 0.45 | 5.0% |
| 0.5 to 0.25 | 22.1% |
| 0.1 to 0.01 | 14.8% |
| 0.5 to none | 2.6% |

The samples around "none to 0.85", for a tone of amplitude 0.5:

```
0.4993 0.4998 | 0.3884 0.4377 0.4741 0.4955 0.5047 0.5058
```

A dip of four samples, about 80 us. The first sample is 0.777 of the one
before it.

The cause is in `core/filters/biquad.cpp`. The filter computes
`output = input*b0 + z1` and keeps `z1` and `z2` when its coefficients change.
`b0` shrinks with gain HF, so the first sample after a change is off by the
change in `b0` times the input. With no filter on the source `DoFilters` in
`core/voice.cpp` clears the state, and a filter at 0.9999 holds almost none,
so the two starts measure the same. Going to "none" is clean, because the
filter is left out.

The engine's smoother, moving gain HF from 1 to 0.1 with its 0.1 s time
constant:

| Updates | Count | Biggest move | Updates above the tone's own 2.6% |
| --- | --- | --- | --- |
| one per frame, 60 a second | 55 | 20.6% | 8 |
| four per frame | 219 | 5.6% | 14 |
| one per frame, no step larger than 0.02 | 82 | 3.8% | 17 |

Attaching the same values again every frame is harmless. Thirty frames
rendered that way are the same bytes as attaching once.

## 5. Reuse

- Stop, a null buffer, a new buffer and play, which is what `AudioSourcePool`
  does when it hands a source on, keep the filter. The next sound's 8 kHz tone
  was at -28.88 dB, the same as the last one's.
- `alSourcei(source, AL_DIRECT_FILTER, AL_FILTER_NULL)` clears it: 0.000 dB.
- One filter object serves every source. Two sources played at once, an 8 kHz
  tone and a 10 kHz tone. The one object was set to 0.1 and attached to the
  first, then set to 0.5 and attached to the second. They measured -28.88 dB
  and -11.46 dB, the same as with a filter object each.
- The object was then set to 1 and deleted. Both sources kept their levels.

## 6. A library with no filters

Not run. No library without EFX could be loaded on this machine. There is no
`OpenAL32.dll` in `System32`, and `GetApi(soft: false)` loaded the packaged
OpenAL Soft again on both systems. The process had one OpenAL module either
way.

What was run is the library's answer to names it does not know.
`alcIsExtensionPresent(device, "ALC_EXT_NOT_A_THING")` is false,
`alGetProcAddress("alGenFiltersNotAThing")` is null, `alGetEnumValue` is 0,
and none of them raises an AL error. By the OpenAL 1.1 specification a library
without EFX answers the real names the same way.

So the check is the extension first, then every pointer against null. The
spike's `Efx.TryLoad` does that and returns null for either.

## 7. NativeAOT

| Build | Runs | Checks | Against the Windows NativeAOT run |
| --- | --- | --- | --- |
| Windows, JIT | 3 | 27 pass | the same lines |
| Windows, NativeAOT | 3 | 27 pass | the same lines |
| Linux, JIT | 3 | 27 pass | the same lines |
| Linux, NativeAOT | 3 | 27 pass | the same lines |

"The same lines" covers every number in sections 1 to 6 and the hashes.

The publish prints six warnings, the same six for `win-x64` and `linux-x64`.
All are `IL3000` or `IL3002`, and all are in
`Silk.NET.Core.Loader.DefaultPathResolver` and
`Microsoft.Extensions.DependencyModel.DependencyContext`: the library loader
behind `GetApi`. None names the spike's code. There is no trimming warning and
no dynamic code warning. The engine's own publish was not run to compare.

The Linux NativeAOT executable was not built on Linux. WSL has a .NET runtime
at `~/spectra-dotnet10` and no SDK. The executable was compiled on Windows and
linked by clang in WSL, through the wrappers the Luau spike left in
`spikes/luau-aot/cross`. It needs `libm` and `libc` only. The packaged
`libopenal.so` needs `libstdc++.so.6` and `libgcc_s.so.1` as well. The JIT
build is one folder for both systems, copied to the Linux filesystem and run
with the runtime there.

## 8. Cost

Nanoseconds unless marked, NativeAOT, the middle one of three runs. The JIT
numbers are within 6 percent. A single call is timed over 200,000 calls, best
of nine rounds. A 32 voice line is the best of 200 frames.

| Measurement | Windows | Linux |
| --- | --- | --- |
| `alFilterf` | 26.3 | 22.7 |
| `alSourcei(AL_DIRECT_FILTER)`, source playing | 127.5 | 111.5 |
| two `alFilterf` and the attach, source playing | 182.6 | 155.9 |
| the same, source not playing | 138.8 | 101.0 |
| for scale: `alSourcef(AL_GAIN)` through Silk.NET, source playing | 92.4 | 87.5 |
| 32 voices: set and attach all of them | 5.80 us | 5.00 us |
| 32 voices: mix 800 samples with no filter | 8.6 us | 4.5 us |
| 32 voices: mix 800 samples, every voice filtered | 60.0 us | 55.5 us |

32 changes a frame take 6 us, 0.04% of a 60 Hz frame. It does not matter. The
engine sets one gain per change, not two, which is about 155 ns.

The filter itself costs more than the calls. It adds about 51 us to the mix of
32 voices for one frame of sound, 0.3% of one core, and that is on OpenAL's
mixer thread, not ours. A voice at gain HF 1 pays nothing.

On a loopback device no mixer thread runs beside these calls. With a real
device one does, and that was not measured.

## Surprises and traps

1. Gain HF is the level at 5 kHz, not the level of "the highs". Half of gain
   HF's decibels are still to come above 5 kHz.
2. Low values eat the middle. At 0.01 a 1 kHz tone is 12 dB down and a 2 kHz
   tone 24 dB. 0 behaves as 0.001 and takes 5 dB off 200 Hz.
3. A gain HF change is a step and puts a dip in the low end. See section 4.
   Keeping a filter attached at 0.9999 does not avoid it.
4. `alFilteri(AL_FILTER_TYPE, ...)` resets the gains. Set the type once, when
   the object is made.
5. A NaN or a value outside 0 to 1 raises an error and changes nothing.
   `OpenAlBackend` reads `alGetError` only when it makes a source or a buffer
   or fills one, so the error would surface there, against the wrong call.
6. A source cannot be asked for its filter, and asking prints to stderr.
7. A 16 bit loopback context has the limiter and the dither on. A float one
   has neither.
8. On a loopback device a sound never ends by itself. It ends when enough
   samples have been rendered.
9. `GetApi(soft: false)` is not "the system's OpenAL". With none installed it
   loads the packaged one.
10. A second `dotnet publish` reuses the object file and prints no IL
    warnings, because the IL compiler does not run. The scripts delete
    `obj/Release/net10.0/<rid>/native` first. Without that the count read 0.

## What this means for the build step

The smallest shape in `OpenAlBackend`:

- In `TryCreate`, after the context is current: if
  `alc.IsExtensionPresent(device, "ALC_EXT_EFX")`, fetch `alGenFilters`,
  `alDeleteFilters`, `alFilteri` and `alFilterf` with `al.GetProcAddress`. If
  all four are there, make one filter, set its type to `AL_FILTER_LOWPASS`,
  and read `alGetError`. Keep the pointers and the filter id as fields of the
  backend. The filter belongs to the device, so nothing here is static.
- If any of that fails, the filter id stays 0 and the backend logs one
  warning: `OpenAL has no EFX filters. Sounds behind walls will be quieter
  but not duller.` The capabilities line already says "EFX no" at Information.
- In `ConfigureSource`, when the filter id is not 0: clamp `GainHf` to 0 to 1
  and treat NaN as 1. At 1 or more, attach `AL_FILTER_NULL`. Below 1, call
  `alFilterf(filter, AL_LOWPASS_GAINHF, value)` and attach the filter. The
  attach is `_al.SetSourceProperty(source, (SourceInteger)0x20005, ...)`.
- That is also the reset. Every sound start goes through `ConfigureSource`
  before its buffer is set, so a pooled source loses its old filter there.
  `AudioSourcePool` needs no change.
- No cache of the last value per source. Attaching unchanged values is the
  same bytes and 32 of them cost 6 us.
- `AL_LOWPASS_GAIN` stays at 1. Loudness keeps going through `AL_GAIN`, which
  OpenAL ramps.
- In `Dispose`, delete the filter before the context goes.

Two decisions the build step has to make:

- What `GainHf` means. Passed straight through it is the level at 5 kHz, and
  the top of the band gets its square. Passing the square root makes it the
  level the top of the band ends at. Either is fine. The doc comment on
  `AudioSourceSettings.GainHf` should say which, with the number 5 kHz.
- How big a step may be. `SoundPathSmoother` moves gain HF by 15% of the
  remaining distance per frame at 60 Hz, so the first step of a full swing is
  0.14 and measures as a 21% dip. A cap of 0.02 per update brought that to
  3.8% and stretched the swing from 55 updates to 82. Whether the uncapped dip
  can be heard on real sounds needs a person to listen.

The tests:

- A second factory with the shape of `AudioBackendFactory`, for example
  `OpenAlBackend.TryCreateLoopback`, and a `Render(Span<float>)` on the
  backend. It fetches the three ALC pointers with `alcGetProcAddress(null,
  ...)`, opens the device with a null name, and creates the context with
  `ALC_FORMAT_CHANNELS_SOFT` = `ALC_MONO_SOFT`, `ALC_FORMAT_TYPE_SOFT` =
  `ALC_FLOAT_SOFT`, `ALC_FREQUENCY` = 48000. Float keeps the limiter and the
  dither out. `Rig.Open` in the spike is this code.
- A test plays a buffer of tones through the real `AudioManager`, renders,
  and reads levels the way `Signal.Amplitude` does. Section 3 has the numbers
  to assert, for example 8 kHz at -19.47 dB and 200 Hz at 0.00 dB for gain HF
  0.25. Assert levels with a tolerance, not hashes: the hashes held across
  builds and systems here, but nothing promises them across library versions.
- Time is the render call. A test that waits for a sound to end renders past
  its end. No sleeping.
- The current context is one per process, so these tests cannot run beside
  each other. Put them in one xUnit collection.
- `SpectraEngine.Bsp.Tests` already has `soft_oal.dll` and `libopenal.so` in
  its output, through its reference to Core. The tests need no sound card. A
  hosted CI runner was not tried.

## Not measured

- A library without EFX. See section 6.
- `dotnet publish` on a Linux host. It needs a Linux .NET 10 SDK with clang,
  or a CI job. The same goes for a run on a hosted CI runner.
- A real device: when a change lands, and what the calls cost with the mixer
  thread running.
- Whether the dip can be heard, on a tone or on real sounds.
- A stereo source through the filter, and a streaming source with queued
  buffers. The filter sits on the source, so neither should differ.
- More than one thread. Every call came from one thread.
- arm64 and macOS.
- Another OpenAL Soft version. The 64 sample ramp, the step and the skipped
  filter at 1 are how 1.23.1 is written, not promises of EFX.
- A machine with an `alsoft.ini`. The file can change the mix.
- Silk.NET's EFX extension package as another way in. It was not tried.

## Appendix: the declarations and one call

Enough to repeat the two results without the spike. `al` and `alc` are the
Silk.NET `AL` and `ALContext` from `GetApi(soft: true)`.

```csharp
// include/AL/efx.h
const int AL_DIRECT_FILTER = 0x20005;
const int AL_FILTER_TYPE = 0x8001;
const int AL_FILTER_NULL = 0x0000;
const int AL_FILTER_LOWPASS = 0x0001;
const int AL_LOWPASS_GAINHF = 0x0002;

// include/AL/alext.h, and alc.h for ALC_FREQUENCY
const int ALC_FREQUENCY = 0x1007;
const int ALC_FORMAT_CHANNELS_SOFT = 0x1990;
const int ALC_FORMAT_TYPE_SOFT = 0x1991;
const int ALC_FLOAT_SOFT = 0x1406;
const int ALC_MONO_SOFT = 0x1500;

// The loopback device. No device is needed to fetch these.
var open = (delegate* unmanaged[Cdecl]<byte*, Device*>)alc.GetProcAddress(null, "alcLoopbackOpenDeviceSOFT");
var render = (delegate* unmanaged[Cdecl]<Device*, void*, int, void>)alc.GetProcAddress(null, "alcRenderSamplesSOFT");

Device* device = open(null);
int* attributes = stackalloc int[]
{
    ALC_FORMAT_CHANNELS_SOFT, ALC_MONO_SOFT,
    ALC_FORMAT_TYPE_SOFT, ALC_FLOAT_SOFT,
    ALC_FREQUENCY, 48000,
    0,
};
Context* context = alc.CreateContext(device, attributes);
alc.MakeContextCurrent(context);

// The filter. A context is current.
bool hasEfx = alc.IsExtensionPresent(device, "ALC_EXT_EFX");
var genFilters = (delegate* unmanaged[Cdecl]<int, uint*, void>)al.GetProcAddress("alGenFilters");
var filteri = (delegate* unmanaged[Cdecl]<uint, int, int, void>)al.GetProcAddress("alFilteri");
var filterf = (delegate* unmanaged[Cdecl]<uint, int, float, void>)al.GetProcAddress("alFilterf");

uint filter = 0;
genFilters(1, &filter);
filteri(filter, AL_FILTER_TYPE, AL_FILTER_LOWPASS);

// Per change: set, then attach. The attach is what copies the value.
filterf(filter, AL_LOWPASS_GAINHF, 0.25f);
al.SetSourceProperty(source, (SourceInteger)AL_DIRECT_FILTER, (int)filter);

// Back to no filter.
al.SetSourceProperty(source, (SourceInteger)AL_DIRECT_FILTER, AL_FILTER_NULL);

// A tenth of a second of the mix.
float[] samples = new float[4800];
fixed (float* data = samples)
    render(device, data, samples.Length);
```

## Files

Everything is under `spikes/openal-filters/`.

| Path | What it is |
| --- | --- |
| `run-windows.ps1` | build, publish, run three times each, compare |
| `run-linux.ps1`, `run-in-wsl.sh` | the Linux NativeAOT publish and the runs in WSL |
| `OpenAlFilterSpike/Efx.cs`, `Loopback.cs` | the function pointers and the constants |
| `OpenAlFilterSpike/Rig.cs` | a loopback device with a current context |
| `OpenAlFilterSpike/Signal.cs` | the test tones and the level measurement |
| `OpenAlFilterSpike/Checks/` | one file per question. `StepJumpChecks.cs` is the low end of question 4. |
