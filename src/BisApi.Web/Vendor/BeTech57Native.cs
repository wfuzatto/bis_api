using System.Runtime.InteropServices;

namespace BisApi.Vendor;

internal static class BeTech57Native
{
    private const string DllName = "btlock57L.dll";

    [DllImport(DllName, EntryPoint = "SerialNo_FromNow", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern int SerialNoFromNow();

    [DllImport(DllName,
        EntryPoint = "Write_Guest_Card",
        CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi,
        ExactSpelling = true)]
    internal static extern int WriteGuestCard(
        byte readerModel,
        byte port,
        byte sectorNo,
        [MarshalAs(UnmanagedType.LPStr)] string hotelPassword,
        int guestSerial,
        int holderSerial,
        int guestIndex,
        [MarshalAs(UnmanagedType.LPStr)] string doorId,
        [MarshalAs(UnmanagedType.LPStr)] string suitDoor,
        [MarshalAs(UnmanagedType.LPStr)] string publicDoor,
        [MarshalAs(UnmanagedType.LPStr)] string beginTime,
        [MarshalAs(UnmanagedType.LPStr)] string endTime);
}
