#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <winscard.h>

#include <algorithm>
#include <array>
#include <cctype>
#include <cstdio>
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
        if (rc != SCARD_S_SUCCESS)
            return {};

        std::vector<std::string> result;
        for (const char* p = multi; p && *p; p += std::strlen(p) + 1)
            result.emplace_back(p);
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
            for (const auto& reader : readers)
            {
                if (_stricmp(reader.c_str(), preferred) == 0)
                {
                    g_reader = reader;
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
                return OK;
            }
        }

        g_reader = readers.front();
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

        if (rc == SCARD_W_REMOVED_CARD || rc == SCARD_E_NO_SMARTCARD || rc == SCARD_E_INVALID_HANDLE)
        {
            DisconnectCard();
            return ERR_NO_CARD;
        }
        if (rc != SCARD_S_SUCCESS)
            return ERR_TRANSMIT;

        response.assign(recv.begin(), recv.begin() + recvLength);
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

extern "C" short __stdcall acr_120Open(BYTE, BYTE* versionLength, BYTE* versionInfo, BYTE* status)
{
    ExclusiveLock lock;
    ReleaseContext();
    short rc = EnsureContextAndReader();
    if (rc != OK)
        return rc;

    static const char version[] = "BIS-PCSC-ACR122-1.0";
    if (versionLength)
        *versionLength = static_cast<BYTE>(sizeof(version) - 1);
    if (versionInfo)
        std::memcpy(versionInfo, version, sizeof(version) - 1);
    if (status)
        *status = 0;
    return OK;
}

extern "C" short __stdcall acr_120Select(BYTE* tagType, BYTE* tagLength, BYTE* serial)
{
    ExclusiveLock lock;
    const BYTE command[] = {0xFF, 0xCA, 0x00, 0x00, 0x00};
    std::vector<BYTE> response;
    short rc = Transmit(command, static_cast<DWORD>(sizeof(command)), response);
    if (rc != OK)
        return rc;
    if (!Is9000(response) || response.size() < 6)
        return ERR_NO_CARD;

    size_t uidLength = response.size() - 2;
    if (uidLength > 10)
        uidLength = 10;
    if (tagType)
        *tagType = uidLength == 4 ? 0x02 : 0x05;
    if (tagLength)
        *tagLength = static_cast<BYTE>(uidLength);
    if (serial)
        std::memcpy(serial, response.data(), uidLength);
    return OK;
}

extern "C" short __stdcall acr_120Read(BYTE, BYTE block, BYTE keyType, BYTE* key, int, BYTE* output16)
{
    ExclusiveLock lock;
    if (!output16)
        return ERR_READ;
    short rc = Authenticate(block, keyType, key);
    if (rc != OK)
        return rc;

    const BYTE command[] = {0xFF, 0xB0, 0x00, block, 0x10};
    std::vector<BYTE> response;
    rc = Transmit(command, static_cast<DWORD>(sizeof(command)), response);
    if (rc != OK || !Is9000(response) || response.size() < 18)
        return ERR_READ;
    std::memcpy(output16, response.data(), 16);
    return OK;
}

extern "C" short __stdcall acr_120Write(BYTE, BYTE block, BYTE keyType, BYTE* key, int, BYTE* data16, int)
{
    ExclusiveLock lock;
    if (!data16)
        return ERR_WRITE;
    short rc = Authenticate(block, keyType, key);
    if (rc != OK)
        return rc;

    std::array<BYTE, 21> command{};
    command[0] = 0xFF;
    command[1] = 0xD6;
    command[2] = 0x00;
    command[3] = block;
    command[4] = 0x10;
    std::copy(data16, data16 + 16, command.begin() + 5);

    std::vector<BYTE> response;
    rc = Transmit(command.data(), static_cast<DWORD>(command.size()), response);
    return rc == OK && Is9000(response) ? OK : ERR_WRITE;
}

extern "C" short __stdcall acr_120Close()
{
    ExclusiveLock lock;
    ReleaseContext();
    return OK;
}

extern "C" short __stdcall acr_120Beep(BYTE)
{
    return OK;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID)
{
    return TRUE;
}
