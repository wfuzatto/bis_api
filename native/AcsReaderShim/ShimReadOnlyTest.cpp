#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <array>
#include <cstdio>
#include <cstring>

template<size_t N> struct Guarded {
    BYTE before = 0xA7;
    std::array<BYTE, N> data{};
    BYTE after = 0xB9;
    bool intact() const { return before == 0xA7 && after == 0xB9; }
};

// Hardware test: only Open, Select (UID APDU), Close. No key or block APIs.
int main(int argc, char** argv) {
    static_assert(sizeof(void*) == 4, "This ABI test must be x86");
    if (argc != 3) return 2;
    SetEnvironmentVariableA("BIS_API_PCSC_READER", argv[2]);
    SetEnvironmentVariableA("BIS_API_SHIM_TRACE", nullptr);
    HMODULE module = LoadLibraryA(argv[1]);
    if (!module) { printf("LoadLibrary error=%lu\n", GetLastError()); return 3; }
    auto open = reinterpret_cast<short(__stdcall*)(BYTE,BYTE*,BYTE*,BYTE*)>(GetProcAddress(module,"acr_120Open"));
    auto select = reinterpret_cast<short(__stdcall*)(BYTE*,BYTE*,BYTE*)>(GetProcAddress(module,"acr_120Select"));
    auto close = reinterpret_cast<short(__stdcall*)()>(GetProcAddress(module,"acr_120Close"));
    if (!open || !select || !close) return 4;
    Guarded<1> length, type, uidLength;
    Guarded<128> version, status;
    Guarded<256> uid;
    short openRc = open(0, length.data.data(), version.data.data(), status.data.data());
    short selectRc = openRc == 0 ? select(type.data.data(), uidLength.data.data(), uid.data.data()) : -1;
    close();
    bool guards = length.intact() && type.intact() && uidLength.intact() && version.intact() && status.intact() && uid.intact();
    printf("open=%d select=%d guards=%s versionLength=%u tagType=%u uidLength=%u\n",
        openRc, selectRc, guards ? "OK" : "FAILED", length.data[0], type.data[0], uidLength.data[0]);
    FreeLibrary(module);
    return openRc == 0 && selectRc == 0 && guards && length.data[0] <= 128 && uidLength.data[0] == 4 ? 0 : 1;
}
