using System.Runtime.InteropServices;

namespace BisApi.Hardware;

internal static class Acr120Native
{
    private const string DllName = "acr120u.dll";

    [DllImport(DllName, EntryPoint = "ACR120_Open", CallingConvention = CallingConvention.Winapi)]
    internal static extern short Open(short readerPort);

    [DllImport(DllName, EntryPoint = "ACR120_Close", CallingConvention = CallingConvention.Winapi)]
    internal static extern short Close(short hReader);

    [DllImport(DllName, EntryPoint = "ACR120_RequestDLLVersion", CallingConvention = CallingConvention.Winapi)]
    internal static extern short RequestDllVersion(ref byte versionLength, [Out] byte[] versionInfo);

    [DllImport(DllName, EntryPoint = "ACR120_Select", CallingConvention = CallingConvention.Winapi)]
    internal static extern short Select(
        short hReader,
        ref byte resultTagType,
        ref byte resultTagLength,
        [Out] byte[] resultSerialNumber);

    [DllImport(DllName, EntryPoint = "ACR120_Login", CallingConvention = CallingConvention.Winapi)]
    internal static extern short Login(
        short hReader,
        byte sector,
        byte keyType,
        sbyte storedNo,
        IntPtr key);

    [DllImport(DllName, EntryPoint = "ACR120_Read", CallingConvention = CallingConvention.Winapi)]
    internal static extern short Read(short hReader, byte block, [Out] byte[] blockData);

    [DllImport(DllName, EntryPoint = "ACR120_Write", CallingConvention = CallingConvention.Winapi)]
    internal static extern short Write(short hReader, byte block, byte[] blockData);
}
