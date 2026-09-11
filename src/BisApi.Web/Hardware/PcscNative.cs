using System.Runtime.InteropServices;

namespace BisApi.Hardware;

internal static class PcscNative
{
    internal const uint ScopeSystem = 2;
    internal const uint ShareShared = 2;
    internal const uint ProtocolT0 = 1;
    internal const uint ProtocolT1 = 2;
    internal const uint LeaveCard = 0;
    internal const uint AttrAtrString = 0x00090303;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ScardIoRequest
    {
        internal uint Protocol;
        internal uint PciLength;
    }

    [DllImport("winscard.dll")]
    internal static extern int SCardEstablishContext(
        uint scope,
        IntPtr reserved1,
        IntPtr reserved2,
        out nuint context);

    [DllImport("winscard.dll")]
    internal static extern int SCardReleaseContext(nuint context);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    internal static extern int SCardListReaders(
        nuint context,
        string? groups,
        IntPtr readers,
        ref uint readerChars);

    [DllImport("winscard.dll", CharSet = CharSet.Unicode)]
    internal static extern int SCardConnect(
        nuint context,
        string readerName,
        uint shareMode,
        uint preferredProtocols,
        out nuint card,
        out uint activeProtocol);

    [DllImport("winscard.dll")]
    internal static extern int SCardDisconnect(nuint card, uint disposition);

    [DllImport("winscard.dll")]
    internal static extern int SCardGetAttrib(
        nuint card,
        uint attributeId,
        [Out] byte[] attribute,
        ref uint attributeLength);

    [DllImport("winscard.dll")]
    internal static extern int SCardTransmit(
        nuint card,
        ref ScardIoRequest sendPci,
        byte[] sendBuffer,
        uint sendLength,
        IntPtr receivePci,
        [Out] byte[] receiveBuffer,
        ref uint receiveLength);
}
