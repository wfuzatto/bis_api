#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winscard.h>

#include <algorithm>
#include <array>
#include <cctype>
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <string>
#include <vector>

#pragma comment(lib, "winscard.lib")

namespace
{
    SRWLOCK g_lock = SRWLOCK_INIT;
    SCARDCONTEXT g_context = 0;
    SCARDHANDLE g_card = 0;
    DWORD g_protocol = 0;
    std::string g_reader;

    constexpr short OK = 0;
    constexpr short ERR_OPEN = -2000;
    constexpr short ERR_NO_READER = -2001;
    constexpr short ERR_NO_CARD = -2002;
    constexpr short ERR_TRANSMIT = -2003;
    constexpr short ERR_AUTH = -2004;
    constexpr short ERR_READ = -2005;
    constexpr short ERR_WRITE = -2006;
    constexpr short ERR_KEY = -2007;

    // Opt-in technical trace. Never pass credentials, keys or block data here.
    void Trace(const char* format, ...)
    {
        const DWORD savedError = GetLastError();
        char enabled[4]{};
        if (GetEnvironmentVariableA("BIS_API_SHIM_TRACE", enabled, sizeof(enabled)) != 1 || enabled[0] != '1')
        {
            SetLastError(savedError);
            return;
        }
        CreateDirectoryW(L"C:\\ProgramData\\BisApi", nullptr);
        CreateDirectoryW(L"C:\\ProgramData\\BisApi\\logs", nullptr);
        HANDLE file = CreateFileW(L"C:\\ProgramData\\BisApi\\logs\\shim-trace.log", FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE)
        {
            SYSTEMTIME time{};
            GetLocalTime(&time);
            char detail[1400]{};
            va_list args;
            va_start(args, format);
            vsnprintf(detail, sizeof(detail), format, args);
            va_end(args);
            char line[1600]{};
            const int length = snprintf(line, sizeof(line),
                "%04u-%02u-%02uT%02u:%02u:%02u.%03u pid=%lu %s\r\n",
                time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute,
                time.wSecond, time.wMilliseconds, GetCurrentProcessId(), detail);
            DWORD written = 0;
            if (length > 0 && length < static_cast<int>(sizeof(line)))
                WriteFile(file, line, static_cast<DWORD>(length), &written, nullptr);
            CloseHandle(file);
        }
        SetLastError(savedError);
    }

    short TraceReturn(const char* name, short rc)
    {
        Trace("RETURN %s = %d", name, static_cast<int>(rc));
        return rc;
    }

    void DisconnectCard()
    {
        if (g_card != 0)
        {
            SCardDisconnect(g_card, SCARD_LEAVE_CARD);
            g_card = 0;
            g_protocol = 0;
        }
    }

    void ReleaseContext()
    {
        DisconnectCard();
        if (g_context != 0)
        {
            SCardReleaseContext(g_context);
            g_context = 0;
        }
        g_reader.clear();
    }

    std::vector<std::string> ListReaders(SCARDCONTEXT context)
    {
        DWORD chars = SCARD_AUTOALLOCATE;
        LPSTR multi = nullptr;
        LONG rc = SCardListReadersA(context, nullptr, reinterpret_cast<LPSTR>(&multi), &chars);
        Trace("SCardListReaders rc=%ld hex=0x%08lX", rc, static_cast<unsigned long>(rc));
        if (rc != SCARD_S_SUCCESS)
            return {};

        std::vector<std::string> result;
        for (const char* p = multi; p && *p; p += std::strlen(p) + 1)
        {
            result.emplace_back(p);
            Trace("enumerated reader=%s", p);
        }
        if (multi)
            SCardFreeMemory(context, multi);
        return result;
    }

    bool ContainsInsensitive(const std::string& value, const std::string& needle)
    {
        auto lower = [](unsigned char c) { return static_cast<char>(std::tolower(c)); };
        std::string a(value.size(), '\0');
        std::string b(needle.size(), '\0');
        std::transform(value.begin(), value.end(), a.begin(), lower);
        std::transform(needle.begin(), needle.end(), b.begin(), lower);
        return a.find(b) != std::string::npos;
    }

    short EnsureContextAndReader()
    {
        if (g_context == 0)
        {
            LONG rc = SCardEstablishContext(SCARD_SCOPE_SYSTEM, nullptr, nullptr, &g_context);
            Trace("SCardEstablishContext rc=%ld hex=0x%08lX", rc, static_cast<unsigned long>(rc));
            if (rc != SCARD_S_SUCCESS)
                return ERR_OPEN;
        }

        if (!g_reader.empty())
            return OK;

        const auto readers = ListReaders(g_context);
        if (readers.empty())
            return ERR_NO_READER;

        char preferred[512]{};
        DWORD count = GetEnvironmentVariableA("BIS_API_PCSC_READER", preferred, static_cast<DWORD>(sizeof(preferred)));
        if (count > 0 && count < sizeof(preferred))
        {
            Trace("requested reader=%s", preferred);
            for (const auto& reader : readers)
            {
                if (_stricmp(reader.c_str(), preferred) == 0)
                {
                    g_reader = reader;
                    Trace("selected reader=%s", g_reader.c_str());
                    return OK;
                }
            }
            return ERR_NO_READER;
        }

        for (const auto& reader : readers)
        {
            if (ContainsInsensitive(reader, "ACR122"))
            {
                g_reader = reader;
                Trace("selected reader=%s", g_reader.c_str());
                return OK;
            }
        }

        g_reader = readers.front();
        Trace("selected reader=%s", g_reader.c_str());
        return OK;
    }

    short EnsureCard()
    {
        short rc = EnsureContextAndReader();
        if (rc != OK)
            return rc;
        if (g_card != 0)
            return OK;

        LONG status = SCardConnectA(
            g_context,
            g_reader.c_str(),
            SCARD_SHARE_SHARED,
            SCARD_PROTOCOL_T0 | SCARD_PROTOCOL_T1,
            &g_card,
            &g_protocol);
        Trace("SCardConnect rc=%ld hex=0x%08lX protocol=%lu", status,
            static_cast<unsigned long>(status), g_protocol);
        if (status == SCARD_E_NO_SMARTCARD || status == SCARD_W_REMOVED_CARD)
            return ERR_NO_CARD;
        if (status != SCARD_S_SUCCESS)
            return ERR_OPEN;
        return OK;
    }

    short Transmit(const BYTE* command, DWORD commandLength, std::vector<BYTE>& response)
    {
        short ensure = EnsureCard();
        if (ensure != OK)
            return ensure;

        SCARD_IO_REQUEST sendPci{};
        sendPci.dwProtocol = g_protocol;
        sendPci.cbPciLength = sizeof(sendPci);

        std::array<BYTE, 512> recv{};
        DWORD recvLength = static_cast<DWORD>(recv.size());
        LONG rc = SCardTransmit(
            g_card,
            &sendPci,
            command,
            commandLength,
            nullptr,
            recv.data(),
            &recvLength);
        Trace("SCardTransmit ins=0x%02X rc=%ld hex=0x%08lX", commandLength > 1 ? command[1] : 0,
            rc, static_cast<unsigned long>(rc));

        if (rc == SCARD_W_REMOVED_CARD || rc == SCARD_E_NO_SMARTCARD || rc == SCARD_E_INVALID_HANDLE)
        {
            DisconnectCard();
            return ERR_NO_CARD;
        }
        if (rc != SCARD_S_SUCCESS)
            return ERR_TRANSMIT;

        response.assign(recv.begin(), recv.begin() + recvLength);
        if (recvLength >= 2)
            Trace("APDU ins=0x%02X SW=%02X%02X", commandLength > 1 ? command[1] : 0,
                recv[recvLength - 2], recv[recvLength - 1]);
        return OK;
    }

    bool Is9000(const std::vector<BYTE>& response)
    {
        return response.size() >= 2 && response[response.size() - 2] == 0x90 && response.back() == 0x00;
    }

    bool ParseHexKey(const char* value, std::array<BYTE, 6>& key)
    {
        if (!value || std::strlen(value) != 12)
            return false;

        auto nibble = [](char c) -> int
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        };

        for (size_t i = 0; i < key.size(); ++i)
        {
            int hi = nibble(value[i * 2]);
            int lo = nibble(value[i * 2 + 1]);
            if (hi < 0 || lo < 0)
                return false;
            key[i] = static_cast<BYTE>((hi << 4) | lo);
        }
        return true;
    }

    bool ReadEnvKey(const char* name, std::array<BYTE, 6>& key)
    {
        char value[64]{};
        DWORD n = GetEnvironmentVariableA(name, value, static_cast<DWORD>(sizeof(value)));
        return n > 0 && n < sizeof(value) && ParseHexKey(value, key);
    }

    short ResolveKey(BYTE keyType, const BYTE* suppliedKey, std::array<BYTE, 6>& key, BYTE& authType)
    {
        authType = (keyType == 1 || keyType == 3 || keyType == 6) ? 0x61 : 0x60;

        switch (keyType)
        {
        case 0:
        case 1:
            if (!suppliedKey)
                return ERR_KEY;
            std::copy(suppliedKey, suppliedKey + 6, key.begin());
            return OK;
        case 2:
            if (!ReadEnvKey("BIS_API_DEFAULT_KEY_A", key))
                key.fill(0xFF);
            return OK;
        case 3:
            if (!ReadEnvKey("BIS_API_DEFAULT_KEY_B", key))
                key.fill(0xFF);
            return OK;
        case 4:
            key.fill(0xFF);
            return OK;
        case 5:
            return ReadEnvKey("BIS_API_STORED_KEY_A", key) ? OK : ERR_KEY;
        case 6:
            return ReadEnvKey("BIS_API_STORED_KEY_B", key) ? OK : ERR_KEY;
        default:
            return ERR_KEY;
        }
    }

    short Authenticate(BYTE block, BYTE keyType, const BYTE* suppliedKey)
    {
        std::array<BYTE, 6> key{};
        BYTE authType = 0x60;
        short rc = ResolveKey(keyType, suppliedKey, key, authType);
        if (rc != OK)
            return rc;

        std::array<BYTE, 11> load{0xFF, 0x82, 0x00, 0x00, 0x06, 0, 0, 0, 0, 0, 0};
        std::copy(key.begin(), key.end(), load.begin() + 5);
        std::vector<BYTE> response;
        rc = Transmit(load.data(), static_cast<DWORD>(load.size()), response);
        if (rc != OK || !Is9000(response))
            return ERR_AUTH;

        const BYTE auth[] = {0xFF, 0x86, 0x00, 0x00, 0x05, 0x01, 0x00, block, authType, 0x00};
        response.clear();
        rc = Transmit(auth, static_cast<DWORD>(sizeof(auth)), response);
        return rc == OK && Is9000(response) ? OK : ERR_AUTH;
    }

    class ExclusiveLock
    {
    public:
        ExclusiveLock() { AcquireSRWLockExclusive(&g_lock); }
        ~ExclusiveLock() { ReleaseSRWLockExclusive(&g_lock); }
    };
}

extern "C" short __stdcall acr_120Open(BYTE port, BYTE* versionLength, BYTE* versionInfo, BYTE* status)
{
    ExclusiveLock lock;
    Trace("ENTER acr_120Open port=%u", port);
    HMODULE module = nullptr;
    char modulePath[MAX_PATH]{};
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCSTR>(&acr_120Open), &module) && GetModuleFileNameA(module, modulePath, MAX_PATH))
        Trace("shim module=%s", modulePath);
    ReleaseContext();
    short rc = EnsureContextAndReader();
    if (rc != OK)
        return TraceReturn("acr_120Open", rc);

    static const char version[] = "BIS-PCSC-ACR122-1.0";
    if (versionLength)
        *versionLength = static_cast<BYTE>(sizeof(version) - 1);
    if (versionInfo)
        std::memcpy(versionInfo, version, sizeof(version) - 1);
    if (status)
        *status = 0;
    return TraceReturn("acr_120Open", OK);
}

extern "C" short __stdcall acr_120Select(BYTE* tagType, BYTE* tagLength, BYTE* serial)
{
    ExclusiveLock lock;
    Trace("ENTER acr_120Select UID_APDU=FF CA 00 00 00");
    const BYTE command[] = {0xFF, 0xCA, 0x00, 0x00, 0x00};
    std::vector<BYTE> response;
    short rc = Transmit(command, static_cast<DWORD>(sizeof(command)), response);
    if (rc != OK)
        return TraceReturn("acr_120Select", rc);
    if (!Is9000(response) || response.size() < 6)
        return TraceReturn("acr_120Select", ERR_NO_CARD);

    size_t uidLength = response.size() - 2;
    if (uidLength > 10)
        uidLength = 10;
    if (tagType)
        *tagType = uidLength == 4 ? 0x02 : 0x05;
    if (tagLength)
        *tagLength = static_cast<BYTE>(uidLength);
    if (serial)
        std::memcpy(serial, response.data(), uidLength);
    char uid[21]{};
    for (size_t i = 0; i < uidLength; ++i)
        snprintf(uid + 2 * i, sizeof(uid) - 2 * i, "%02X", response[i]);
    Trace("UID=%s length=%u", uid, static_cast<unsigned>(uidLength));
    return TraceReturn("acr_120Select", OK);
}

extern "C" short __stdcall acr_120Read(BYTE sector, BYTE block, BYTE keyType, BYTE* key, int, BYTE* output16)
{
    ExclusiveLock lock;
    Trace("ENTER acr_120Read sector=%u block=%u keyType=%u", sector, block, keyType);
    if (!output16)
        return TraceReturn("acr_120Read", ERR_READ);
    short rc = Authenticate(block, keyType, key);
    Trace("acr_120Read authentication result=%d", rc);
    if (rc != OK)
        return TraceReturn("acr_120Read", rc);

    const BYTE command[] = {0xFF, 0xB0, 0x00, block, 0x10};
    std::vector<BYTE> response;
    rc = Transmit(command, static_cast<DWORD>(sizeof(command)), response);
    if (rc != OK || !Is9000(response) || response.size() < 18)
        return TraceReturn("acr_120Read", ERR_READ);
    std::memcpy(output16, response.data(), 16);
    return TraceReturn("acr_120Read", OK);
}

extern "C" short __stdcall acr_120Write(BYTE sector, BYTE block, BYTE keyType, BYTE* key, int, BYTE* data16, int)
{
    ExclusiveLock lock;
    Trace("ENTER acr_120Write sector=%u block=%u keyType=%u", sector, block, keyType);
    if (!data16)
        return TraceReturn("acr_120Write", ERR_WRITE);
    short rc = Authenticate(block, keyType, key);
    Trace("acr_120Write authentication result=%d", rc);
    if (rc != OK)
        return TraceReturn("acr_120Write", rc);

    std::array<BYTE, 21> command{};
    command[0] = 0xFF;
    command[1] = 0xD6;
    command[2] = 0x00;
    command[3] = block;
    command[4] = 0x10;
    std::copy(data16, data16 + 16, command.begin() + 5);

    std::vector<BYTE> response;
    Trace("acr_120Write sending update block=%u", block);
    rc = Transmit(command.data(), static_cast<DWORD>(command.size()), response);
    return TraceReturn("acr_120Write", rc == OK && Is9000(response) ? OK : ERR_WRITE);
}

extern "C" short __stdcall acr_120Close()
{
    ExclusiveLock lock;
    Trace("ENTER acr_120Close");
    ReleaseContext();
    return TraceReturn("acr_120Close", OK);
}

extern "C" short __stdcall acr_120Beep(BYTE)
{
    return OK;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}
