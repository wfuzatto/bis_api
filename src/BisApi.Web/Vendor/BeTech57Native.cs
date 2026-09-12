using System.Runtime.InteropServices;

namespace BisApi.Vendor;

internal static class BeTech57Native
{
    private const string DllName = "btlock57L.dll";

    [DllImport(DllName, EntryPoint = "SerialNo_FromNow", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern int SerialNoFromNow();

    [DllImport(DllName, EntryPoint = "Read_Snr", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    internal static extern int ReadSnr(byte port, byte readerModel,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 8)] byte[] outputSerial);

    // Verified from btlock57L.dll x86: stdcall / ret 0x30.  The output fields
    // mirror the guest-card record fields used by Write_Guest_Card.  HPASS is
    // an input to the codec only; callers must never return or log it.
    [DllImport(DllName,
        EntryPoint = "Read_Guest_Card",
        CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi,
        ExactSpelling = true)]
    internal static extern int ReadGuestCard(
        byte port,
        byte readerModel,
        byte sectorNo,
        [MarshalAs(UnmanagedType.LPStr)] string hotelPassword,
        out int guestSerial,
        out int holderSerial,
        out int guestIndex,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 7)] byte[] doorId,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 5)] byte[] suitDoor,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 9)] byte[] publicDoor,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 11)] byte[] beginTime,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 11)] byte[] endTime);

    [DllImport(DllName,
        EntryPoint = "Write_Guest_Card",
        CallingConvention = CallingConvention.StdCall,
        CharSet = CharSet.Ansi,
        ExactSpelling = true)]
    internal static extern int WriteGuestCard(
        byte port,
        byte readerModel,
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
