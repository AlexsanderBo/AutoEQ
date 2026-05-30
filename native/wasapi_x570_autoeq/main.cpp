#include <Windows.h>
#undef min
#undef max
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <functiondiscoverykeys_devpkey.h>
#include <propvarutil.h>
#include <ksmedia.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <complex>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iomanip>
#include <iostream>
#include <limits>
#include <map>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "uuid.lib")
#pragma comment(lib, "winmm.lib")

namespace {
constexpr int kNativeSampleRate = 48000;
constexpr int kChannels = 2;
constexpr int kBitsPerSample = 32;
constexpr int kFftSize = 4096;
constexpr int kHopSize = kFftSize / 2;
constexpr int kBandCount = 100;
constexpr REFERENCE_TIME kStableBufferDuration100ns = 10000000; // 100ms stable fallback for ALC1220-VB shared mode.
constexpr REFERENCE_TIME kLowLatencyBufferDuration100ns = 200000; // 20ms low-latency target for Realtek shared WASAPI.
constexpr double kPi = 3.14159265358979323846264338327950288;
constexpr double kMinFrequency = 20.0;
constexpr double kMaxFrequency = 20000.0;
constexpr double kMaxEqGainDb = 12.0;

std::wstring widen(const std::string& value) {
    if (value.empty()) return L"";
    int size = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
    std::wstring result(size - 1, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, result.data(), size);
    return result;
}

std::string narrow(const std::wstring& value) {
    if (value.empty()) return "";
    int size = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string result(size - 1, '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, result.data(), size, nullptr, nullptr);
    return result;
}

std::string hresultHex(HRESULT hr) {
    std::ostringstream oss;
    oss << "0x" << std::hex << std::uppercase << std::setw(8) << std::setfill('0') << static_cast<unsigned long>(hr);
    return oss.str();
}

bool failed(HRESULT hr, const char* where) {
    if (SUCCEEDED(hr)) return false;
    std::cerr << "[WASAPI] " << where << " failed: " << hresultHex(hr) << std::endl;
    return true;
}

template <typename T>
void safeRelease(T*& ptr) {
    if (ptr) { ptr->Release(); ptr = nullptr; }
}

std::string jsonEscape(const std::string& input) {
    std::ostringstream oss;
    for (char c : input) {
        if (c == '\\') oss << "\\\\";
        else if (c == '"') oss << "\\\"";
        else if (c == '\n') oss << "\\n";
        else if (c == '\r') oss << "\\r";
        else if (c == '\t') oss << "\\t";
        else oss << c;
    }
    return oss.str();
}

struct AudioChunk {
    std::vector<float> interleaved;
    UINT32 frames = 0;
    UINT32 channels = kChannels;
    int sampleRate = kNativeSampleRate;
    double rms = 0.0;
};

struct SpectrumFrame {
    std::vector<double> bandsDb;
    std::vector<double> bandCentersHz;
    double bassDb = -120.0;
    double midDb = -120.0;
    double trebleDb = -120.0;
    double imbalanceScore = 0.0;
    double rms = 0.0;
};

struct EqCurve {
    std::vector<double> gainsDb;
    std::string profileName;
    double confidence = 0.0;
};

struct BiquadSection {
    double b0 = 1.0, b1 = 0.0, b2 = 0.0, a1 = 0.0, a2 = 0.0;
    double z1[2] = {0.0, 0.0};
    double z2[2] = {0.0, 0.0};
};

enum class LatencyProfile {
    Low,
    Stable
};

const char* latencyProfileName(LatencyProfile profile) {
    return profile == LatencyProfile::Low ? "Low Latency 20ms" : "Stable 100ms";
}

REFERENCE_TIME latencyDuration100ns(LatencyProfile profile) {
    return profile == LatencyProfile::Low ? kLowLatencyBufferDuration100ns : kStableBufferDuration100ns;
}

double durationMs(REFERENCE_TIME duration) {
    return static_cast<double>(duration) / 10000.0;
}

class WasapiOptimizedForX570AorusUltra {
public:
    ~WasapiOptimizedForX570AorusUltra() { cleanup(); }

    bool initialize(bool enableRender, const std::wstring& inputHint = L"", const std::wstring& outputHint = L"", LatencyProfile latencyProfile = LatencyProfile::Low) {
        renderEnabled_ = enableRender;
        requestedLatencyProfile_ = latencyProfile;
        activeBufferDuration100ns_ = latencyDuration100ns(latencyProfile);
        HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        if (hr == RPC_E_CHANGED_MODE) hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        if (failed(hr, "CoInitializeEx")) return false;
        comInitialized_ = true;

        hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator_));
        if (failed(hr, "CoCreateInstance(MMDeviceEnumerator)")) return false;

        captureDevice_ = inputHint.empty() ? getDefaultDevice(eRender, eConsole) : findDeviceByName(eRender, inputHint);
        if (!captureDevice_) return false;
        captureDeviceName_ = getDeviceName(captureDevice_);
        std::cout << "[Device] Capture endpoint: " << narrow(captureDeviceName_) << std::endl;
        verifyRealtek(captureDeviceName_);

        hr = captureDevice_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(&captureAudioClient_));
        if (failed(hr, "Activate capture IAudioClient")) return false;
        if (!configureFormat(captureAudioClient_)) return false;
        printDevicePeriod(captureAudioClient_, "Capture");

        captureEvent_ = CreateEvent(nullptr, FALSE, FALSE, nullptr);
        if (!captureEvent_) return false;

        // Shared + loopback captures the render endpoint after Windows audio engine post-processing.
        DWORD captureFlags = AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK;
        hr = initializeSharedEventClient(captureAudioClient_, captureFlags, "capture loopback");
        if (FAILED(hr)) return false;
        hr = captureAudioClient_->SetEventHandle(captureEvent_);
        if (failed(hr, "IAudioClient::SetEventHandle(capture)")) return false;
        hr = captureAudioClient_->GetService(IID_PPV_ARGS(&captureClient_));
        if (failed(hr, "IAudioClient::GetService(IAudioCaptureClient)")) return false;
        captureAudioClient_->GetBufferSize(&captureBufferFrames_);

        if (renderEnabled_) {
            renderDevice_ = outputHint.empty() ? getDefaultDevice(eRender, eConsole) : findDeviceByName(eRender, outputHint);
            if (!renderDevice_ || !initializeRenderClient()) return false;
        }

        printConfiguration();
        return true;
    }

    bool start() {
        HRESULT hr = captureAudioClient_->Start();
        if (failed(hr, "IAudioClient::Start(capture)")) return false;
        if (renderEnabled_ && renderAudioClient_) {
            hr = renderAudioClient_->Start();
            if (failed(hr, "IAudioClient::Start(render)")) return false;
        }
        return true;
    }

    void stop() {
        if (captureAudioClient_) captureAudioClient_->Stop();
        if (renderAudioClient_) renderAudioClient_->Stop();
    }

    bool captureAudio(AudioChunk& chunk, DWORD timeoutMs = 2000) {
        chunk = AudioChunk{};
        if (WaitForSingleObject(captureEvent_, timeoutMs) != WAIT_OBJECT_0) return false;

        UINT32 packetFrames = 0;
        HRESULT hr = captureClient_->GetNextPacketSize(&packetFrames);
        if (failed(hr, "IAudioCaptureClient::GetNextPacketSize")) return false;

        while (packetFrames > 0) {
            BYTE* data = nullptr;
            UINT32 frames = 0;
            DWORD flags = 0;
            hr = captureClient_->GetBuffer(&data, &frames, &flags, nullptr, nullptr);
            if (failed(hr, "IAudioCaptureClient::GetBuffer")) return false;

            size_t samples = static_cast<size_t>(frames) * waveFormat_->nChannels;
            size_t oldSize = chunk.interleaved.size();
            chunk.interleaved.resize(oldSize + samples);
            if (flags & AUDCLNT_BUFFERFLAGS_SILENT) {
                std::fill(chunk.interleaved.begin() + oldSize, chunk.interleaved.end(), 0.0f);
            } else if (isFloatFormat()) {
                const float* src = reinterpret_cast<const float*>(data);
                std::copy(src, src + samples, chunk.interleaved.begin() + oldSize);
            } else {
                copyPcmToFloat(data, frames, chunk.interleaved.data() + oldSize);
            }

            hr = captureClient_->ReleaseBuffer(frames);
            if (failed(hr, "IAudioCaptureClient::ReleaseBuffer")) return false;
            chunk.frames += frames;
            hr = captureClient_->GetNextPacketSize(&packetFrames);
            if (failed(hr, "IAudioCaptureClient::GetNextPacketSize(loop)")) return false;
        }

        chunk.channels = waveFormat_->nChannels;
        chunk.sampleRate = waveFormat_->nSamplesPerSec;
        chunk.rms = calculateRms(chunk.interleaved);
        return chunk.frames > 0;
    }

    bool getRenderBuffer(const std::vector<float>& interleaved, UINT32 frames) {
        if (!renderEnabled_ || !renderClient_ || frames == 0) return false;
        UINT32 padding = 0;
        HRESULT hr = renderAudioClient_->GetCurrentPadding(&padding);
        if (failed(hr, "IAudioClient::GetCurrentPadding(render)")) return false;
        UINT32 available = renderBufferFrames_ > padding ? renderBufferFrames_ - padding : 0;
        UINT32 framesToWrite = std::min(frames, available);
        if (framesToWrite == 0) return true;
        BYTE* data = nullptr;
        hr = renderClient_->GetBuffer(framesToWrite, &data);
        if (failed(hr, "IAudioRenderClient::GetBuffer")) return false;
        std::memcpy(data, interleaved.data(), static_cast<size_t>(framesToWrite) * waveFormat_->nBlockAlign);
        hr = renderClient_->ReleaseBuffer(framesToWrite, 0);
        return !failed(hr, "IAudioRenderClient::ReleaseBuffer");
    }

    void cleanup() {
        stop();
        if (captureEvent_) { CloseHandle(captureEvent_); captureEvent_ = nullptr; }
        if (renderEvent_) { CloseHandle(renderEvent_); renderEvent_ = nullptr; }
        if (waveFormat_) { CoTaskMemFree(waveFormat_); waveFormat_ = nullptr; }
        safeRelease(captureClient_);
        safeRelease(renderClient_);
        safeRelease(captureAudioClient_);
        safeRelease(renderAudioClient_);
        safeRelease(captureDevice_);
        safeRelease(renderDevice_);
        safeRelease(enumerator_);
        if (comInitialized_) { CoUninitialize(); comInitialized_ = false; }
    }

    int sampleRate() const { return waveFormat_ ? static_cast<int>(waveFormat_->nSamplesPerSec) : kNativeSampleRate; }
    UINT32 channels() const { return waveFormat_ ? waveFormat_->nChannels : kChannels; }
    std::string deviceName() const { return narrow(captureDeviceName_); }

private:
    IMMDevice* getDefaultDevice(EDataFlow flow, ERole role) {
        IMMDevice* device = nullptr;
        HRESULT hr = enumerator_->GetDefaultAudioEndpoint(flow, role, &device);
        if (failed(hr, "IMMDeviceEnumerator::GetDefaultAudioEndpoint")) return nullptr;
        return device;
    }

    IMMDevice* findDeviceByName(EDataFlow flow, const std::wstring& hint) {
        IMMDeviceCollection* collection = nullptr;
        HRESULT hr = enumerator_->EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, &collection);
        if (failed(hr, "IMMDeviceEnumerator::EnumAudioEndpoints")) return nullptr;
        UINT count = 0;
        collection->GetCount(&count);
        for (UINT i = 0; i < count; ++i) {
            IMMDevice* device = nullptr;
            collection->Item(i, &device);
            std::wstring name = getDeviceName(device), lowerName = name, lowerHint = hint;
            std::transform(lowerName.begin(), lowerName.end(), lowerName.begin(), ::towlower);
            std::transform(lowerHint.begin(), lowerHint.end(), lowerHint.begin(), ::towlower);
            if (lowerName.find(lowerHint) != std::wstring::npos) { safeRelease(collection); return device; }
            safeRelease(device);
        }
        safeRelease(collection);
        std::wcerr << L"[Device] No endpoint matched hint: " << hint << std::endl;
        return nullptr;
    }

    std::wstring getDeviceName(IMMDevice* device) {
        IPropertyStore* store = nullptr;
        PROPVARIANT value;
        PropVariantInit(&value);
        if (FAILED(device->OpenPropertyStore(STGM_READ, &store))) return L"Unknown endpoint";
        HRESULT hr = store->GetValue(PKEY_Device_FriendlyName, &value);
        std::wstring name = SUCCEEDED(hr) && value.vt == VT_LPWSTR ? value.pwszVal : L"Unknown endpoint";
        PropVariantClear(&value);
        safeRelease(store);
        return name;
    }

    void verifyRealtek(const std::wstring& name) {
        std::wstring lower = name;
        std::transform(lower.begin(), lower.end(), lower.begin(), ::towlower);
        if (lower.find(L"realtek") == std::wstring::npos)
            std::cout << "[Verify] Warning: endpoint name does not contain Realtek. Expected ALC1220-VB on X570 AORUS ULTRA." << std::endl;
        else
            std::cout << "[Verify] Realtek endpoint detected. Target codec: ALC1220-VB." << std::endl;
    }

    bool configureFormat(IAudioClient* client) {
        WAVEFORMATEXTENSIBLE desired{};
        desired.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
        desired.Format.nChannels = kChannels;
        desired.Format.nSamplesPerSec = kNativeSampleRate;
        desired.Format.wBitsPerSample = kBitsPerSample;
        desired.Format.nBlockAlign = desired.Format.nChannels * desired.Format.wBitsPerSample / 8;
        desired.Format.nAvgBytesPerSec = desired.Format.nSamplesPerSec * desired.Format.nBlockAlign;
        desired.Format.cbSize = sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX);
        desired.Samples.wValidBitsPerSample = kBitsPerSample;
        desired.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
        desired.SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;

        WAVEFORMATEX* closest = nullptr;
        HRESULT hr = client->IsFormatSupported(AUDCLNT_SHAREMODE_SHARED, reinterpret_cast<WAVEFORMATEX*>(&desired), &closest);
        if (hr == S_OK) {
            waveFormat_ = reinterpret_cast<WAVEFORMATEX*>(CoTaskMemAlloc(sizeof(WAVEFORMATEXTENSIBLE)));
            std::memcpy(waveFormat_, &desired, sizeof(WAVEFORMATEXTENSIBLE));
            std::cout << "[Format] Using hardcoded X570/ALC1220-VB format: 48000 Hz, stereo, IEEE float." << std::endl;
            return true;
        }
        if (closest) CoTaskMemFree(closest);
        std::cout << "[Format] Custom IEEE float unsupported (" << hresultHex(hr) << "). Falling back to mix format." << std::endl;
        hr = client->GetMixFormat(&waveFormat_);
        return !failed(hr, "IAudioClient::GetMixFormat");
    }

    bool initializeRenderClient() {
        std::cout << "[Device] Render endpoint: " << narrow(getDeviceName(renderDevice_)) << std::endl;
        HRESULT hr = renderDevice_->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(&renderAudioClient_));
        if (failed(hr, "Activate render IAudioClient")) return false;
        printDevicePeriod(renderAudioClient_, "Render");
        renderEvent_ = CreateEvent(nullptr, FALSE, FALSE, nullptr);
        if (!renderEvent_) return false;
        // Render is opt-in. Use Virtual Cable -> native -> Realtek to avoid echo/double audio.
        hr = initializeSharedEventClient(renderAudioClient_, AUDCLNT_STREAMFLAGS_EVENTCALLBACK, "render");
        if (FAILED(hr)) return false;
        hr = renderAudioClient_->SetEventHandle(renderEvent_);
        if (failed(hr, "IAudioClient::SetEventHandle(render)")) return false;
        hr = renderAudioClient_->GetService(IID_PPV_ARGS(&renderClient_));
        if (failed(hr, "IAudioClient::GetService(IAudioRenderClient)")) return false;
        renderAudioClient_->GetBufferSize(&renderBufferFrames_);
        return true;
    }

    HRESULT initializeSharedEventClient(IAudioClient* client, DWORD flags, const char* label) {
        HRESULT hr = client->Initialize(AUDCLNT_SHAREMODE_SHARED, flags, activeBufferDuration100ns_, 0, waveFormat_, nullptr);
        if (SUCCEEDED(hr)) return hr;

        std::cerr << "[Latency] " << label << " init failed at " << durationMs(activeBufferDuration100ns_)
                  << "ms (" << hresultHex(hr) << "). Falling back to Stable 100ms." << std::endl;
        if (activeBufferDuration100ns_ == kStableBufferDuration100ns) return hr;

        activeBufferDuration100ns_ = kStableBufferDuration100ns;
        activeLatencyProfile_ = LatencyProfile::Stable;
        hr = client->Initialize(AUDCLNT_SHAREMODE_SHARED, flags, activeBufferDuration100ns_, 0, waveFormat_, nullptr);
        if (failed(hr, "IAudioClient::Initialize(shared event stable fallback)")) return hr;
        return hr;
    }

    void printDevicePeriod(IAudioClient* client, const char* label) const {
        REFERENCE_TIME defaultPeriod = 0;
        REFERENCE_TIME minimumPeriod = 0;
        HRESULT hr = client->GetDevicePeriod(&defaultPeriod, &minimumPeriod);
        if (SUCCEEDED(hr)) {
            std::cout << "[Latency] " << label << " device period: default=" << durationMs(defaultPeriod)
                      << "ms minimum=" << durationMs(minimumPeriod) << "ms" << std::endl;
        }
    }

    bool isFloatFormat() const {
        return waveFormat_->wFormatTag == WAVE_FORMAT_EXTENSIBLE &&
            reinterpret_cast<WAVEFORMATEXTENSIBLE*>(waveFormat_)->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
    }

    void copyPcmToFloat(const BYTE* data, UINT32 frames, float* dst) const {
        UINT32 channels = waveFormat_->nChannels;
        if (waveFormat_->wBitsPerSample == 16) {
            const int16_t* src = reinterpret_cast<const int16_t*>(data);
            for (size_t i = 0; i < static_cast<size_t>(frames) * channels; ++i) dst[i] = src[i] / 32768.0f;
        } else if (waveFormat_->wBitsPerSample == 24) {
            for (size_t i = 0; i < static_cast<size_t>(frames) * channels; ++i) {
                const BYTE* s = data + i * 3;
                int32_t v = s[0] | (s[1] << 8) | (s[2] << 16);
                if (v & 0x800000) v |= ~0xFFFFFF;
                dst[i] = static_cast<float>(v / 8388608.0);
            }
        } else if (waveFormat_->wBitsPerSample == 32) {
            const int32_t* src = reinterpret_cast<const int32_t*>(data);
            for (size_t i = 0; i < static_cast<size_t>(frames) * channels; ++i) dst[i] = src[i] / 2147483648.0f;
        } else {
            std::fill(dst, dst + static_cast<size_t>(frames) * channels, 0.0f);
        }
    }

    double calculateRms(const std::vector<float>& samples) const {
        if (samples.empty()) return 0.0;
        double sum = 0.0;
        for (float s : samples) sum += static_cast<double>(s) * s;
        return std::sqrt(sum / samples.size());
    }

    void printConfiguration() const {
        std::cout << "[Config] Motherboard: Gigabyte X570 AORUS ULTRA Rev. 1.0/1.1/1.2" << std::endl;
        std::cout << "[Config] Codec target: Realtek ALC1220-VB, DSD capable on back-panel line out" << std::endl;
        std::cout << "[Config] Share mode: AUDCLNT_SHAREMODE_SHARED" << std::endl;
        std::cout << "[Config] Capture flags: AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK" << std::endl;
        std::cout << "[Config] Requested latency: " << latencyProfileName(requestedLatencyProfile_) << std::endl;
        std::cout << "[Config] Active latency: " << latencyProfileName(activeLatencyProfile_) << std::endl;
        std::cout << "[Config] Buffer duration: " << activeBufferDuration100ns_ << " x 100ns (" << durationMs(activeBufferDuration100ns_) << "ms)" << std::endl;
        std::cout << "[Format] SampleRate=" << waveFormat_->nSamplesPerSec << " Channels=" << waveFormat_->nChannels
                  << " Bits=" << waveFormat_->wBitsPerSample << " BlockAlign=" << waveFormat_->nBlockAlign << std::endl;
    }

    IMMDeviceEnumerator* enumerator_ = nullptr;
    IMMDevice* captureDevice_ = nullptr;
    IMMDevice* renderDevice_ = nullptr;
    IAudioClient* captureAudioClient_ = nullptr;
    IAudioClient* renderAudioClient_ = nullptr;
    IAudioCaptureClient* captureClient_ = nullptr;
    IAudioRenderClient* renderClient_ = nullptr;
    WAVEFORMATEX* waveFormat_ = nullptr;
    HANDLE captureEvent_ = nullptr;
    HANDLE renderEvent_ = nullptr;
    UINT32 captureBufferFrames_ = 0;
    UINT32 renderBufferFrames_ = 0;
    bool comInitialized_ = false;
    bool renderEnabled_ = false;
    LatencyProfile requestedLatencyProfile_ = LatencyProfile::Low;
    LatencyProfile activeLatencyProfile_ = LatencyProfile::Low;
    REFERENCE_TIME activeBufferDuration100ns_ = kLowLatencyBufferDuration100ns;
    std::wstring captureDeviceName_;
};

class FFTAnalyzer {
public:
    explicit FFTAnalyzer(int sampleRate = kNativeSampleRate) : sampleRate_(sampleRate) {
        hann_.resize(kFftSize);
        for (int i = 0; i < kFftSize; ++i) hann_[i] = 0.5 * (1.0 - std::cos(2.0 * kPi * i / (kFftSize - 1)));
        bandCenters_ = bucketToFrequencies();
    }

    SpectrumFrame analyze(const AudioChunk& chunk) {
        appendMono(chunk);
        SpectrumFrame frame;
        frame.bandsDb.assign(kBandCount, -120.0);
        frame.bandCentersHz = bandCenters_;
        frame.rms = chunk.rms;
        if (monoBuffer_.size() < kFftSize) return frame;

        std::vector<std::complex<double>> bins(kFftSize);
        for (int i = 0; i < kFftSize; ++i) bins[i] = {monoBuffer_[i] * hann_[i], 0.0};
        fft(bins);
        monoBuffer_.erase(monoBuffer_.begin(), monoBuffer_.begin() + std::min<size_t>(kHopSize, monoBuffer_.size()));

        std::vector<double> power(kFftSize / 2, 0.0);
        for (int i = 1; i < kFftSize / 2; ++i) power[i] = std::norm(bins[i]);
        frame.bandsDb = bandsFromPower(power);
        detectImbalance(frame);
        return frame;
    }

    std::vector<double> bucketToFrequencies() const {
        std::vector<double> centers(kBandCount);
        double ratio = std::pow(kMaxFrequency / kMinFrequency, 1.0 / (kBandCount - 1));
        for (int i = 0; i < kBandCount; ++i) centers[i] = kMinFrequency * std::pow(ratio, i);
        return centers;
    }

    void detectImbalance(SpectrumFrame& frame) const {
        frame.bassDb = averageRange(frame, 20.0, 250.0);
        frame.midDb = averageRange(frame, 250.0, 4000.0);
        frame.trebleDb = averageRange(frame, 4000.0, 20000.0);
        frame.imbalanceScore = std::abs(frame.bassDb - frame.midDb) + std::abs(frame.trebleDb - frame.midDb);
    }

private:
    void appendMono(const AudioChunk& chunk) {
        for (UINT32 f = 0; f < chunk.frames; ++f) {
            double sum = 0.0;
            for (UINT32 c = 0; c < chunk.channels; ++c) sum += chunk.interleaved[static_cast<size_t>(f) * chunk.channels + c];
            monoBuffer_.push_back(sum / chunk.channels);
        }
        if (monoBuffer_.size() > kFftSize * 4) monoBuffer_.erase(monoBuffer_.begin(), monoBuffer_.end() - kFftSize * 4);
    }

    void fft(std::vector<std::complex<double>>& a) const {
        size_t n = a.size();
        for (size_t i = 1, j = 0; i < n; ++i) {
            size_t bit = n >> 1;
            for (; j & bit; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) std::swap(a[i], a[j]);
        }
        for (size_t len = 2; len <= n; len <<= 1) {
            std::complex<double> wlen(std::cos(-2.0 * kPi / len), std::sin(-2.0 * kPi / len));
            for (size_t i = 0; i < n; i += len) {
                std::complex<double> w(1.0, 0.0);
                for (size_t j = 0; j < len / 2; ++j) {
                    auto u = a[i + j], v = a[i + j + len / 2] * w;
                    a[i + j] = u + v;
                    a[i + j + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }
    }

    std::vector<double> bandsFromPower(const std::vector<double>& power) const {
        std::vector<double> bands(kBandCount, -120.0);
        double binHz = static_cast<double>(sampleRate_) / kFftSize;
        double edgeRatio = std::pow(kMaxFrequency / kMinFrequency, 1.0 / kBandCount);
        for (int b = 0; b < kBandCount; ++b) {
            double low = bandCenters_[b] / std::sqrt(edgeRatio), high = bandCenters_[b] * std::sqrt(edgeRatio);
            int loBin = std::max(1, static_cast<int>(std::floor(low / binHz)));
            int hiBin = std::min(static_cast<int>(power.size() - 1), static_cast<int>(std::ceil(high / binHz)));
            double sum = 0.0;
            int count = 0;
            for (int i = loBin; i <= hiBin; ++i) { sum += power[i]; ++count; }
            if (count > 0) bands[b] = 10.0 * std::log10(sum / count + 1e-18);
        }
        return bands;
    }

    double averageRange(const SpectrumFrame& frame, double low, double high) const {
        double sum = 0.0;
        int count = 0;
        for (size_t i = 0; i < frame.bandsDb.size(); ++i) {
            if (frame.bandCentersHz[i] >= low && frame.bandCentersHz[i] < high) { sum += frame.bandsDb[i]; ++count; }
        }
        return count ? sum / count : -120.0;
    }

    int sampleRate_;
    std::vector<double> hann_;
    std::vector<double> monoBuffer_;
    std::vector<double> bandCenters_;
};

class AutoEQAlgorithm {
public:
    AutoEQAlgorithm() {
        profiles_["Woburn 3 Clean Warm"] = makeTarget(-1.5, 1.5, -0.5);
        profiles_["Near Wall Less Boom"] = makeTarget(-4.0, 1.0, 0.0);
        profiles_["Clear Vocal"] = makeTarget(-2.0, 3.0, 1.0);
        smoothed_.assign(kBandCount, 0.0);
    }

    EqCurve calculateAutoEQ(const SpectrumFrame& current) {
        std::string profile = findClosestProfile(current);
        const auto& target = profiles_[profile];
        std::vector<double> raw(kBandCount, 0.0);
        for (int i = 0; i < kBandCount; ++i) {
            double relative = current.bandsDb[i] - current.midDb;
            raw[i] = std::clamp(target[i] - relative, -kMaxEqGainDb, kMaxEqGainDb);
        }
        smoothTransition(raw);
        return EqCurve{smoothed_, profile, confidenceFor(current)};
    }

    std::string findClosestProfile(const SpectrumFrame& current) const {
        double best = std::numeric_limits<double>::max();
        std::string bestName = profiles_.begin()->first;
        for (const auto& item : profiles_) {
            double distance = 0.0;
            for (int i = 0; i < kBandCount; ++i) {
                double d = (current.bandsDb[i] - current.midDb) - item.second[i];
                distance += d * d;
            }
            if (distance < best) { best = distance; bestName = item.first; }
        }
        return bestName;
    }

    void smoothTransition(const std::vector<double>& target) {
        constexpr double alpha = 1.0 / 100.0; // About 5 seconds at 20 updates/sec.
        for (int i = 0; i < kBandCount; ++i) smoothed_[i] += (target[i] - smoothed_[i]) * alpha;
    }

private:
    std::vector<double> makeTarget(double bass, double mid, double treble) const {
        std::vector<double> target(kBandCount, 0.0);
        auto centers = FFTAnalyzer().bucketToFrequencies();
        for (int i = 0; i < kBandCount; ++i) target[i] = centers[i] < 250.0 ? bass : (centers[i] < 4000.0 ? mid : treble);
        return target;
    }

    double confidenceFor(const SpectrumFrame& current) const {
        double signal = std::clamp((20.0 * std::log10(current.rms + 1e-9) + 60.0) / 60.0, 0.0, 1.0);
        double balance = std::clamp(1.0 - current.imbalanceScore / 80.0, 0.0, 1.0);
        return 0.65 * signal + 0.35 * balance;
    }

    std::map<std::string, std::vector<double>> profiles_;
    std::vector<double> smoothed_;
};

class BiquadEQ {
public:
    explicit BiquadEQ(int sampleRate = kNativeSampleRate) : sampleRate_(sampleRate) {
        bandsHz_ = FFTAnalyzer(sampleRate).bucketToFrequencies();
        sections_.resize(kBandCount);
        designBiquad(std::vector<double>(kBandCount, 0.0));
    }

    void designBiquad(const std::vector<double>& gainsDb) {
        for (int i = 0; i < kBandCount; ++i) {
            double f0 = std::clamp(bandsHz_[i], 20.0, sampleRate_ * 0.45);
            double q = 4.318; // Approx 1/3-octave peaking EQ bandwidth.
            double a = std::pow(10.0, gainsDb[i] / 40.0);
            double w0 = 2.0 * kPi * f0 / sampleRate_;
            double alpha = std::sin(w0) / (2.0 * q);
            double cosw0 = std::cos(w0);
            double b0 = 1.0 + alpha * a, b1 = -2.0 * cosw0, b2 = 1.0 - alpha * a;
            double a0 = 1.0 + alpha / a, a1 = -2.0 * cosw0, a2 = 1.0 - alpha / a;
            sections_[i].b0 = b0 / a0;
            sections_[i].b1 = b1 / a0;
            sections_[i].b2 = b2 / a0;
            sections_[i].a1 = a1 / a0;
            sections_[i].a2 = a2 / a0;
        }
    }

    void applyEQ(std::vector<float>& interleaved, UINT32 frames, UINT32 channels) {
        for (UINT32 f = 0; f < frames; ++f) {
            for (UINT32 c = 0; c < channels && c < 2; ++c) {
                double x = interleaved[static_cast<size_t>(f) * channels + c];
                for (auto& s : sections_) {
                    double y = s.b0 * x + s.z1[c];
                    s.z1[c] = s.b1 * x - s.a1 * y + s.z2[c];
                    s.z2[c] = s.b2 * x - s.a2 * y;
                    x = y;
                }
                interleaved[static_cast<size_t>(f) * channels + c] = static_cast<float>(std::clamp(x, -1.0, 1.0));
            }
        }
    }

private:
    int sampleRate_;
    std::vector<double> bandsHz_;
    std::vector<BiquadSection> sections_;
};

class AutoEQSystem {
public:
    bool initialize(bool processMode, const std::wstring& inputHint, const std::wstring& outputHint, LatencyProfile latencyProfile) {
        processMode_ = processMode;
        if (!wasapi_.initialize(processMode, inputHint, outputHint, latencyProfile)) return false;
        analyzer_ = std::make_unique<FFTAnalyzer>(wasapi_.sampleRate());
        biquad_ = std::make_unique<BiquadEQ>(wasapi_.sampleRate());
        return true;
    }

    int run(int seconds) {
        HANDLE mutex = CreateMutexW(nullptr, TRUE, L"Global\\WoburnAutoEQ_WASAPI_X570");
        if (mutex && GetLastError() == ERROR_ALREADY_EXISTS) {
            std::cerr << "Another WoburnAutoEQ WASAPI native instance is already running." << std::endl;
            CloseHandle(mutex);
            return 2;
        }
        if (!wasapi_.start()) return 1;

        auto start = std::chrono::steady_clock::now();
        while (seconds <= 0 || std::chrono::duration_cast<std::chrono::seconds>(std::chrono::steady_clock::now() - start).count() < seconds) {
            AudioChunk chunk;
            if (!wasapi_.captureAudio(chunk)) continue;
            SpectrumFrame spectrum = analyzer_->analyze(chunk);
            EqCurve eq = autoEq_.calculateAutoEQ(spectrum);
            biquad_->designBiquad(eq.gainsDb);
            if (processMode_) {
                biquad_->applyEQ(chunk.interleaved, chunk.frames, chunk.channels);
                wasapi_.getRenderBuffer(chunk.interleaved, chunk.frames);
            }
            printJson(spectrum, eq);
        }
        wasapi_.stop();
        if (mutex) { ReleaseMutex(mutex); CloseHandle(mutex); }
        return 0;
    }

private:
    void printJson(const SpectrumFrame& s, const EqCurve& eq) const {
        std::cout << "{\"device\":\"" << jsonEscape(wasapi_.deviceName()) << "\","
                  << "\"sampleRate\":" << wasapi_.sampleRate() << ","
                  << "\"channels\":" << wasapi_.channels() << ","
                  << "\"rms\":" << std::fixed << std::setprecision(6) << s.rms << ","
                  << "\"bassDb\":" << std::setprecision(2) << s.bassDb << ","
                  << "\"midDb\":" << s.midDb << ","
                  << "\"trebleDb\":" << s.trebleDb << ","
                  << "\"profile\":\"" << jsonEscape(eq.profileName) << "\","
                  << "\"confidence\":" << std::setprecision(3) << eq.confidence << ",\"eqGainsDb\":[";
        for (size_t i = 0; i < eq.gainsDb.size(); ++i) { if (i) std::cout << ","; std::cout << std::setprecision(2) << eq.gainsDb[i]; }
        std::cout << "],\"bandCentersHz\":[";
        for (size_t i = 0; i < s.bandCentersHz.size(); ++i) { if (i) std::cout << ","; std::cout << std::setprecision(1) << s.bandCentersHz[i]; }
        std::cout << "]}" << std::endl;
    }

    WasapiOptimizedForX570AorusUltra wasapi_;
    std::unique_ptr<FFTAnalyzer> analyzer_;
    AutoEQAlgorithm autoEq_;
    std::unique_ptr<BiquadEQ> biquad_;
    bool processMode_ = false;
};

void printUsage() {
    std::cout << "WASAPI X570 AORUS ULTRA + Realtek ALC1220-VB AutoEQ native module\n"
              << "Usage:\n"
              << "  wasapi_x570_autoeq.exe --analyze [--latency low|stable] [--seconds N]\n"
              << "  wasapi_x570_autoeq.exe --process --input \"Virtual Cable\" --output \"Realtek\" [--latency low|stable] [--seconds N]\n\n"
              << "Modes:\n"
              << "  --analyze  Safe default. WASAPI loopback capture + FFT + AutoEQ JSON. WPF/Equalizer APO applies EQ.\n"
              << "  --process  Experimental. Use only with a separate input route to avoid echo/double audio.\n\n"
              << "Latency:\n"
              << "  --latency low     20ms native low-latency target, with automatic 100ms fallback.\n"
              << "  --latency stable  100ms stable fallback profile.\n";
}

} // namespace

int main(int argc, char** argv) {
    bool processMode = false;
    int seconds = 0;
    std::wstring inputHint;
    std::wstring outputHint;
    LatencyProfile latencyProfile = LatencyProfile::Low;

    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg == "--help" || arg == "-h") { printUsage(); return 0; }
        if (arg == "--process") processMode = true;
        else if (arg == "--analyze") processMode = false;
        else if (arg == "--seconds" && i + 1 < argc) seconds = std::max(0, std::atoi(argv[++i]));
        else if (arg == "--input" && i + 1 < argc) inputHint = widen(argv[++i]);
        else if (arg == "--output" && i + 1 < argc) outputHint = widen(argv[++i]);
        else if (arg == "--latency" && i + 1 < argc) {
            std::string value = argv[++i];
            std::transform(value.begin(), value.end(), value.begin(), ::tolower);
            if (value == "stable") latencyProfile = LatencyProfile::Stable;
            else if (value == "low") latencyProfile = LatencyProfile::Low;
            else std::cerr << "[Config] Unknown latency profile '" << value << "'. Using low." << std::endl;
        }
    }

    if (processMode && inputHint.empty()) {
        std::cout << "[Safety] --process should use --input with a virtual endpoint. Continuing with default only for testing." << std::endl;
    }

    AutoEQSystem system;
    if (!system.initialize(processMode, inputHint, outputHint, latencyProfile)) return 1;
    return system.run(seconds);
}