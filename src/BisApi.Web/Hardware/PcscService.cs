using System.Runtime.InteropServices;

namespace BisApi.Hardware;

public sealed class PcscService
{
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

            var atr = new byte[64];
            uint atrLen = (uint)atr.Length;
            ThrowIfError(PcscNative.SCardGetAttrib(card, PcscNative.AttrAtrString, atr, ref atrLen), "SCardGetAttrib(ATR)");

            var uidResponse = Transmit(card, protocol, [0xFF, 0xCA, 0x00, 0x00, 0x00]);
            if (uidResponse.Length < 2 || uidResponse[^2] != 0x90 || uidResponse[^1] != 0x00)
                throw new PcscException("GET UID", uidResponse.Length >= 2
                    ? (uidResponse[^2] << 8) | uidResponse[^1]
                    : -1,
                    "O ACR122 respondeu ao PC/SC, mas o comando de leitura do UID não retornou 90 00.");

            var uid = uidResponse.AsSpan(0, uidResponse.Length - 2).ToArray();
            return new PcscCardInfo(
                reader,
                protocol == PcscNative.ProtocolT1 ? "T=1" : protocol == PcscNative.ProtocolT0 ? "T=0" : $"0x{protocol:X}",
                Convert.ToHexString(atr.AsSpan(0, checked((int)atrLen))),
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

        return readers.FirstOrDefault(x => x.Contains("ACR122", StringComparison.OrdinalIgnoreCase))
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
