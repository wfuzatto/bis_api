using System.Runtime.InteropServices;
using System.Text;

namespace BisApi.Hardware;

public sealed class PcscService
{
    private static readonly byte[] NfcKeyLabAid = [0xF0, 0x4E, 0x46, 0x43, 0x4B, 0x45, 0x59, 0x31];

    public IReadOnlyList<string> ListReaders()
    {
        EnsureWindows();
        nuint context = 0;
        try
        {
            ThrowIfError(PcscNative.SCardEstablishContext(
                PcscNative.ScopeSystem,
                IntPtr.Zero,
                IntPtr.Zero,
                out context), "SCardEstablishContext");

            uint chars = 0;
            var rc = PcscNative.SCardListReaders(context, null, IntPtr.Zero, ref chars);
            if (unchecked((uint)rc) == 0x8010002E) // SCARD_E_NO_READERS_AVAILABLE
                return Array.Empty<string>();
            ThrowIfError(rc, "SCardListReaders(size)");
            if (chars == 0)
                return Array.Empty<string>();

            var bytes = checked((int)chars * 2);
            var buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                ThrowIfError(PcscNative.SCardListReaders(context, null, buffer, ref chars), "SCardListReaders");
                var text = Marshal.PtrToStringUni(buffer, checked((int)chars)) ?? string.Empty;
                return text.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            if (context != 0)
                PcscNative.SCardReleaseContext(context);
        }
    }

    public PcscCardInfo Probe(string? requestedReader = null)
    {
        EnsureWindows();
        nuint context = 0;
        nuint card = 0;
        try
        {
            ThrowIfError(PcscNative.SCardEstablishContext(
                PcscNative.ScopeSystem,
                IntPtr.Zero,
                IntPtr.Zero,
                out context), "SCardEstablishContext");

            var readers = ListReadersWithContext(context);
            var reader = PickReader(readers, requestedReader);
            if (reader is null)
                throw new InvalidOperationException("Nenhum leitor PC/SC foi encontrado. Instale o driver do ACR122U e confirme se o serviço Smart Card do Windows está ativo.");

            ThrowIfError(PcscNative.SCardConnect(
                context,
                reader,
                PcscNative.ShareShared,
                PcscNative.ProtocolT0 | PcscNative.ProtocolT1,
                out card,
                out var protocol), "SCardConnect");

            var atr = ReadAtr(card);
            var uidResponse = Transmit(card, protocol, [0xFF, 0xCA, 0x00, 0x00, 0x00]);
            if (!Is9000(uidResponse))
                throw new PcscException("GET UID", StatusWord(uidResponse),
                    "O ACR122 respondeu ao PC/SC, mas o comando de leitura do UID não retornou 90 00.");

            var uid = uidResponse.AsSpan(0, uidResponse.Length - 2).ToArray();
            return new PcscCardInfo(
                reader,
                ProtocolName(protocol),
                Convert.ToHexString(atr),
                Convert.ToHexString(uid),
                "9000");
        }
        finally
        {
            if (card != 0)
                PcscNative.SCardDisconnect(card, PcscNative.LeaveCard);
            if (context != 0)
                PcscNative.SCardReleaseContext(context);
        }
    }

    public PcscHceProbeInfo ProbeNfcKeyHce(string? requestedReader = null)
    {
        EnsureWindows();
        nuint context = 0;
        nuint card = 0;
        try
        {
            ThrowIfError(PcscNative.SCardEstablishContext(
                PcscNative.ScopeSystem,
                IntPtr.Zero,
                IntPtr.Zero,
                out context), "SCardEstablishContext");

            var readers = ListReadersWithContext(context);
            var reader = PickReader(readers, requestedReader);
            if (reader is null)
                throw new InvalidOperationException("Nenhum leitor PC/SC foi encontrado.");

            ThrowIfError(PcscNative.SCardConnect(
                context,
                reader,
                PcscNative.ShareShared,
                PcscNative.ProtocolT0 | PcscNative.ProtocolT1,
                out card,
                out var protocol), "SCardConnect");

            var atr = ReadAtr(card);

            string? uidHex = null;
            string? uidStatus = null;
            try
            {
                var uidResponse = Transmit(card, protocol, [0xFF, 0xCA, 0x00, 0x00, 0x00]);
                uidStatus = StatusWordHex(uidResponse);
                if (Is9000(uidResponse) && uidResponse.Length > 2)
                    uidHex = Convert.ToHexString(uidResponse.AsSpan(0, uidResponse.Length - 2));
            }
            catch (PcscException)
            {
                // UID is diagnostic only. Some HCE targets/readers do not expose it through FF CA.
            }

            var select = new byte[6 + NfcKeyLabAid.Length];
            select[0] = 0x00;
            select[1] = 0xA4;
            select[2] = 0x04;
            select[3] = 0x00;
            select[4] = (byte)NfcKeyLabAid.Length;
            Buffer.BlockCopy(NfcKeyLabAid, 0, select, 5, NfcKeyLabAid.Length);
            select[^1] = 0x00;

            var selectCommand = select;
            var selectResponse = Transmit(card, protocol, selectCommand);
            if (!Is9000(selectResponse))
            {
                selectCommand = select[..^1];
                selectResponse = Transmit(card, protocol, selectCommand);
            }
            var selectOk = Is9000(selectResponse);

            byte[] labResponse = [];
            if (selectOk)
                labResponse = Transmit(card, protocol, [0x80, 0xCA, 0x00, 0x00, 0x00]);

            var labOk = Is9000(labResponse);
            var labPayload = labOk && labResponse.Length > 2
                ? labResponse.AsSpan(0, labResponse.Length - 2).ToArray()
                : Array.Empty<byte>();

            return new PcscHceProbeInfo(
                reader,
                ProtocolName(protocol),
                Convert.ToHexString(atr),
                uidHex,
                uidStatus,
                Convert.ToHexString(selectCommand),
                Convert.ToHexString(selectResponse),
                StatusWordHex(selectResponse),
                selectOk,
                labResponse.Length == 0 ? null : Convert.ToHexString(labResponse),
                labResponse.Length == 0 ? null : StatusWordHex(labResponse),
                labOk ? Encoding.UTF8.GetString(labPayload) : null,
                selectOk && labOk);
        }
        finally
        {
            if (card != 0)
                PcscNative.SCardDisconnect(card, PcscNative.LeaveCard);
            if (context != 0)
                PcscNative.SCardReleaseContext(context);
        }
    }

    private static byte[] ReadAtr(nuint card)
    {
        var atr = new byte[64];
        uint atrLen = (uint)atr.Length;
        ThrowIfError(PcscNative.SCardGetAttrib(card, PcscNative.AttrAtrString, atr, ref atrLen), "SCardGetAttrib(ATR)");
        return atr.AsSpan(0, checked((int)atrLen)).ToArray();
    }

    private static byte[] Transmit(nuint card, uint protocol, byte[] command)
    {
        var pci = new PcscNative.ScardIoRequest
        {
            Protocol = protocol,
            PciLength = (uint)Marshal.SizeOf<PcscNative.ScardIoRequest>()
        };
        var response = new byte[258];
        uint responseLen = (uint)response.Length;
        ThrowIfError(PcscNative.SCardTransmit(
            card,
            ref pci,
            command,
            (uint)command.Length,
            IntPtr.Zero,
            response,
            ref responseLen), "SCardTransmit");
        return response.AsSpan(0, checked((int)responseLen)).ToArray();
    }

    private static bool Is9000(byte[] response) =>
        response.Length >= 2 && response[^2] == 0x90 && response[^1] == 0x00;

    private static int StatusWord(byte[] response) =>
        response.Length >= 2 ? (response[^2] << 8) | response[^1] : -1;

    private static string? StatusWordHex(byte[] response) =>
        response.Length >= 2 ? $"{response[^2]:X2}{response[^1]:X2}" : null;

    private static string ProtocolName(uint protocol) =>
        protocol == PcscNative.ProtocolT1 ? "T=1" :
        protocol == PcscNative.ProtocolT0 ? "T=0" : $"0x{protocol:X}";

    private static IReadOnlyList<string> ListReadersWithContext(nuint context)
    {
        uint chars = 0;
        var rc = PcscNative.SCardListReaders(context, null, IntPtr.Zero, ref chars);
        if (unchecked((uint)rc) == 0x8010002E)
            return Array.Empty<string>();
        ThrowIfError(rc, "SCardListReaders(size)");
        if (chars == 0)
            return Array.Empty<string>();

        var buffer = Marshal.AllocHGlobal(checked((int)chars * 2));
        try
        {
            ThrowIfError(PcscNative.SCardListReaders(context, null, buffer, ref chars), "SCardListReaders");
            var text = Marshal.PtrToStringUni(buffer, checked((int)chars)) ?? string.Empty;
            return text.Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? PickReader(IReadOnlyList<string> readers, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var exact = readers.FirstOrDefault(x => string.Equals(x, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is null)
                throw new InvalidOperationException($"Leitor PC/SC configurado não foi encontrado: {requested}");
            return exact;
        }

        return readers.FirstOrDefault(x => x.Contains("ACR1281", StringComparison.OrdinalIgnoreCase))
               ?? readers.FirstOrDefault(x => x.Contains("ACR122", StringComparison.OrdinalIgnoreCase))
               ?? readers.FirstOrDefault();
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PC/SC deste projeto usa WinSCard e requer Windows.");
    }

    private static void ThrowIfError(int result, string operation)
    {
        if (result != 0)
            throw new PcscException(operation, result);
    }
}

public sealed record PcscCardInfo(
    string Reader,
    string Protocol,
    string AtrHex,
    string UidHex,
    string StatusWord);

public sealed record PcscHceProbeInfo(
    string Reader,
    string Protocol,
    string AtrHex,
    string? UidHex,
    string? UidStatusWord,
    string SelectAidCommandHex,
    string SelectAidResponseHex,
    string? SelectAidStatusWord,
    bool SelectAidOk,
    string? LabResponseHex,
    string? LabStatusWord,
    string? LabText,
    bool HceRoundTripOk);

public sealed class PcscException : Exception
{
    public PcscException(string operation, int errorCode, string? message = null)
        : base(message ?? $"{operation} falhou. PC/SC: 0x{unchecked((uint)errorCode):X8}")
    {
        Operation = operation;
        ErrorCode = errorCode;
    }

    public string Operation { get; }
    public int ErrorCode { get; }
}
