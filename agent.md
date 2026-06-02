# Agent Context â€” AutoEQ

## NgÆ°á»i dÃ¹ng / xÆ°ng hÃ´

- NgÆ°á»i dÃ¹ng: **BÃ´**.
- Khi há»— trá»£ dá»± Ã¡n nÃ y, gá»i ngÆ°á»i dÃ¹ng lÃ  **BÃ´**.
- Æ¯u tiÃªn tráº£ lá»i báº±ng tiáº¿ng Viá»‡t, ngáº¯n gá»n, rÃµ viá»‡c cáº§n lÃ m.

## Tá»•ng quan dá»± Ã¡n

AutoEQ là app Windows desktop chạy local cho mọi thiết bị output Windows qua OutputAudioProfile.

Má»¥c tiÃªu Ã¢m thanh:

- Sáº¡ch, áº¥m, vocal tiáº¿n.
- Bass gá»n, Ã­t Ã¹/boom.
- Treble má»m, khÃ´ng chÃ³i.

App khÃ´ng dÃ¹ng machine learning, cloud AI, upload audio, hoáº·c external API. Má»i phÃ¢n tÃ­ch Ã¢m thanh cháº¡y local trÃªn mÃ¡y ngÆ°á»i dÃ¹ng.

## Luá»“ng chÃ­nh

```text
YouTube / Chrome / system audio
      -> WASAPI loopback capture vá»›i NAudio
      -> DSP Analyzer
      -> Preset Engine theo OutputAudioProfile
      -> ghi AutoEQ_autoeq.txt
      -> Equalizer APO Ã¡p EQ system-wide
```

## Tech stack

- .NET 8
- WPF
- NAudio: WASAPI loopback audio capture
- MathNet.Numerics: FFT / DSP utilities
- Hardcodet.NotifyIcon.Wpf: WPF system tray support (MIT/free)
- Equalizer APO: backend EQ system-wide
- xUnit: tests
- C++17 native WASAPI module tại `native/wasapi_autoeq/main.cpp`

## Repo free/open-source Ä‘Æ°á»£c phÃ©p tham kháº£o / dÃ¹ng

- `jaakkopasanen/AutoEq` â€” MIT â€” tham kháº£o parametric EQ filters, target curve, Equalizer APO export format. KhÃ´ng import headphone dataset cho Marshall speaker profile.
- `naudio/NAudio` â€” MIT â€” dependency WASAPI loopback capture Ä‘ang dÃ¹ng.
- `mathnet/mathnet-numerics` â€” MIT â€” dependency FFT/DSP Ä‘ang dÃ¹ng.
- `hardcodet/wpf-notifyicon` â€” MIT â€” dependency tray icon cho WPF, dÃ¹ng cho Open AutoEQ / Bypass / Clean Warm / Night Mode / Exit.
- `psidex/EACS` â€” MIT â€” tham kháº£o safe Equalizer APO config switching, khÃ´ng vendor/copy code náº¿u khÃ´ng cáº§n.
- `mirror/equalizerapo` â€” GPL-2.0 â€” chá»‰ lÃ  external backend reference. User tá»± cÃ i Equalizer APO; AutoEQ khÃ´ng embed/copy Equalizer APO code.

TrÃ¡nh repo tráº£ phÃ­, license mÆ¡ há»“, hoáº·c repo SEO/download khÃ´ng rÃµ nguá»“n. Náº¿u thÃªm dependency má»›i, Æ°u tiÃªn free + MIT/Apache/BSD.

## Native WASAPI module

File chÃ­nh:

```text
native/wasapi_autoeq/main.cpp
```

Thiáº¿t káº¿ cho Gigabyte X570 AORUS ULTRA + Realtek ALC1220-VB:

- `AUDCLNT_SHAREMODE_SHARED`
- `AUDCLNT_STREAMFLAGS_LOOPBACK`
- `AUDCLNT_STREAMFLAGS_EVENTCALLBACK`
- 48 kHz, stereo, 32-bit IEEE float khi endpoint há»— trá»£
- Fallback endpoint mix format
- 4096-point manual FFT, Hann window, 50% overlap, 100 bands 20 Hzâ€“20 kHz

Khuyáº¿n nghá»‹ dÃ¹ng analyzer mode, khÃ´ng dÃ¹ng `--process` trÃªn cÃ¹ng default Realtek endpoint náº¿u khÃ´ng muá»‘n render test/double audio.

## Cáº¥u trÃºc thÆ° má»¥c quan trá»ng

```text
AutoEQ/
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  AutoEQ.csproj
  Config/
    AppConfig.cs
  Models/
    AudioFeatures.cs
    AudioOutputInfo.cs
    EqPreset.cs
    NativeAutoEqSnapshot.cs
    OutputAudioProfile.cs
  Services/
    AppLogger.cs
    AudioCaptureService.cs
    DspAnalyzer.cs
    EqualizerApoManager.cs
    NativeWasapiAutoEqClient.cs
    NowPlayingService.cs
    OutputAudioProfileStore.cs
    PresetEngine.cs
    SystemVolumeService.cs

AutoEQ.Tests/
  DspAnalyzerTests.cs
  UnitTest1.cs
  AutoEQ.Tests.csproj

native/
  wasapi_autoeq/
    main.cpp
```

## Vai trÃ² file chÃ­nh

- `AutoEQ/MainWindow.xaml`: UI chÃ­nh. Layout hiá»‡n táº¡i dÃ¹ng 3 cá»™t:
  - TrÃ¡i: Auto EQ, output Ä‘ang dÃ¹ng, preset/output, tuá»³ chá»n nghe, volume knob.
  - Giá»¯a: trÃ¬nh phÃ¡t nháº¡c, Ä‘Ä©a than, media controls.
  - Pháº£i: phÃ¢n tÃ­ch Ã¢m thanh live, phá»• táº§n 7 dáº£i, EQ curve, energy/brightness, bass/vocal/treble.
- `AutoEQ/MainWindow.xaml.cs`: orchestration UI, timers, event handlers, cáº­p nháº­t waveform/analysis/media/volume.
- `Services/AudioCaptureService.cs`: capture audio loopback.
- `Services/DspAnalyzer.cs`: trÃ­ch Ä‘áº·c trÆ°ng Ã¢m thanh.
- `Services/PresetEngine.cs`: chá»n preset rule-based.
- `Services/EqualizerApoManager.cs`: ghi file Equalizer APO vÃ  include config.
- `Services/NativeWasapiAutoEqClient.cs`: gá»i/Ä‘á»c snapshot tá»« native analyzer.
- `Services/NowPlayingService.cs`: Ä‘á»c media session Ä‘ang phÃ¡t.
- `Services/SystemVolumeService.cs`: Ä‘á»c/Ä‘iá»u khiá»ƒn Ã¢m lÆ°á»£ng há»‡ thá»‘ng.
- `Services/OutputAudioProfileStore.cs`: lÆ°u profile output.
- `Services/AppLogger.cs`: log cho UI.

## Equalizer APO

ÄÆ°á»ng dáº«n cáº¥u hÃ¬nh máº·c Ä‘á»‹nh:

```text
C:\Program Files\EqualizerAPO\config
```

App ghi file:

```text
C:\Program Files\EqualizerAPO\config\AutoEQ_autoeq.txt
```

App thÃªm dÃ²ng include vÃ o `config.txt`:

```text
Include: AutoEQ_autoeq.txt
```

TrÆ°á»›c khi sá»­a `config.txt`, app táº¡o backup:

```text
C:\Program Files\EqualizerAPO\config\config.txt.bak
```

KhÃ´ng xoÃ¡ hoáº·c overwrite config ngÆ°á»i dÃ¹ng ngoÃ i file generated `AutoEQ_autoeq.txt` vÃ  include line cáº§n thiáº¿t.

Náº¿u khÃ´ng ghi Ä‘Æ°á»£c APO folder, hÆ°á»›ng dáº«n user cháº¡y app as Administrator má»™t láº§n.

## Presets hiá»‡n cÃ³

- AutoEQ 3 - Clean Warm
- AutoEQ 3 - Near Wall
- Less Boom
- Clear Vocal
- Soft Treble
- Night Mode
- Bypass

## Lá»‡nh dev

Cháº¡y dev:

```bat
run_dev.bat
```

Build solution:

```bat
dotnet build AutoEQ.sln
```

Test solution:

```bat
dotnet test AutoEQ.sln --no-build
```

Build portable folder:

```bat
build.bat
```

Output portable dá»± kiáº¿n:

```text
F:\autoEQ\dist\AutoEQ
F:\autoEQ\dist\AutoEQ\AutoEQ.exe
```

Build native vá»›i GCC/MinGW:

```bat
g++ -std=c++17 native\wasapi_autoeq\main.cpp -lole32 -luuid -lwinmm -o wasapi_autoeq.exe
```

Build native vá»›i Visual Studio Developer Command Prompt:

```bat
cl /EHsc /std:c++17 native\wasapi_autoeq\main.cpp /link ole32.lib uuid.lib winmm.lib /OUT:wasapi_autoeq.exe
```

Run native analyzer 10 giÃ¢y:

```bat
wasapi_autoeq.exe --analyze --seconds 10
```

## Quy Æ°á»›c khi chá»‰nh dá»± Ã¡n

- Giá»¯ app local-only, khÃ´ng thÃªm cloud/network/audio upload náº¿u BÃ´ khÃ´ng yÃªu cáº§u rÃµ.
- Vá»›i UI, trÃ¡nh trÃ¹ng láº·p thÃ´ng tin vÃ  trÃ¡nh quÃ¡ nhiá»u gradient/glow lÃ m rá»‘i.
- Æ¯u tiÃªn layout rÃµ vai trÃ²: Ä‘iá»u khiá»ƒn, trÃ¬nh phÃ¡t, phÃ¢n tÃ­ch live.
- Sau khi sá»­a XAML/C#, cháº¡y build tá»‘i thiá»ƒu:

```bat
dotnet build AutoEQ/AutoEQ.csproj -c Debug --nologo -v q
```

- Khi sá»­a logic DSP/preset, cháº¡y tests:

```bat
dotnet test AutoEQ.sln --no-build
```

## ThÆ° má»¥c táº¡m / validation

CÃ¡c thÆ° má»¥c nhÆ° sau thÆ°á»ng lÃ  output build/validation, khÃ´ng pháº£i source chÃ­nh:

```text
_*_validation_bin/
_*_validation_obj/
AutoEQ/bin/
AutoEQ/obj/
AutoEQ.Tests/bin/
AutoEQ.Tests/obj/
```
