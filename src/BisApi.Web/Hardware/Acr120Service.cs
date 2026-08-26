using System.Runtime.InteropServices;
using System.Text;

namespace BisApi.Hardware;

public sealed class Acr120Service : IDisposable
{
    private readonly object _sync = new();
    private short? _handle;
    private short? _readerPort;

    public bool IsOpen
    {
        get { lock (_sync) return _handle.HasValue; }
    }

    public short? ReaderPort
    {
        get { lock (_sync) return _readerPort; }
    }

    public string ExpectedDllPath => Path.Combine(AppContext.BaseDirectory, "acr120u.dll");

    public bool DllPresent => File.Exists(ExpectedDllPath);

    public string GetDllVersion()
    {
        lock (_sync)
        {
            EnsureWindowsX86();
            var buffer = new byte[64];
            byte len = 0;
            var rc = Acr120Native.RequestDllVersion(ref len, buffer);
            ThrowIfError(rc, "ACR120_RequestDLLVersion");
            var safeLength = Math.Min((int)len, buffer.Length);
            return Encoding.ASCII.GetString(buffer, 0, safeLength).TrimEnd('\0', ' ');
        }
    }

    public ReaderOpenResult Open(short port)
    {
        lock (_sync)
        {
            EnsureWindowsX86();

            if (_handle.HasValue)
                CloseInternal();

            var handle = Acr120Native.Open(port);
            if (handle < 0)
                throw new Acr120Exception("ACR120_Open", handle);

            _handle = handle;
            _readerPort = port;
            return new ReaderOpenResult(true, handle, port);
        }
    }

    public void Close()
    {
        lock (_sync)
            CloseInternal();
    }

    public CardIdentity SelectCard()
    {
        lock (_sync)
        {
            var handle = RequireHandle();
            byte tagType = 0;
            byte tagLength = 0;
            var serial = new byte[10];
            var rc = Acr120Native.Select(handle, ref tagType, ref tagLength, serial);
            ThrowIfError(rc, "ACR120_Select");

            var actualLength = Math.Min((int)tagLength, serial.Length);
            return new CardIdentity(
                tagType,
                tagLength,
                Convert.ToHexString(serial.AsSpan(0, actualLength)));
        }
    }

    public void Login(byte sector, MifareKeyType keyType, string? keyHex)
    {
        lock (_sync)
        {
            if (sector > 39)
                throw new ArgumentOutOfRangeException(nameof(sector), "Sector inválido para MIFARE Classic.");

            var handle = RequireHandle();
            IntPtr keyPtr = IntPtr.Zero;
            try
            {
                if (keyType is MifareKeyType.CustomA or MifareKeyType.CustomB)
                {
                    var key = ParseHex(keyHex, 6, "keyHex");
                    keyPtr = Marshal.AllocHGlobal(6);
                    Marshal.Copy(key, 0, keyPtr, key.Length);
                }

                var rc = Acr120Native.Login(handle, sector, (byte)keyType, 0, keyPtr);
                ThrowIfError(rc, "ACR120_Login");
            }
            finally
            {
                if (keyPtr != IntPtr.Zero)
                    Marshal.FreeHGlobal(keyPtr);
            }
        }
    }

    public BlockReadResult ReadBlock(byte block)
    {
        lock (_sync)
        {
            var handle = RequireHandle();
            var data = new byte[16];
            var rc = Acr120Native.Read(handle, block, data);
            ThrowIfError(rc, "ACR120_Read");
            return new BlockReadResult(block, Convert.ToHexString(data), IsTrailerBlock(block));
        }
    }

    public SectorDumpResult DumpSector(byte sector, MifareKeyType keyType, string? keyHex)
    {
        lock (_sync)
        {
            if (sector > 15)
                throw new ArgumentOutOfRangeException(nameof(sector), "O dump de laboratório está limitado a MIFARE 1K (setores 0-15).");

            Login(sector, keyType, keyHex);
            var blocks = new List<BlockReadResult>(4);
            var firstBlock = sector * 4;

            for (var offset = 0; offset < 4; offset++)
                blocks.Add(ReadBlock((byte)(firstBlock + offset)));

            return new SectorDumpResult(sector, blocks);
        }
    }

    public void WriteBlock(byte block, string dataHex)
    {
        lock (_sync)
        {
            var handle = RequireHandle();
            var data = ParseHex(dataHex, 16, "dataHex");
            var rc = Acr120Native.Write(handle, block, data);
            ThrowIfError(rc, "ACR120_Write");
        }
    }

    public void Dispose()
    {
        lock (_sync)
            CloseInternal();
    }

    public static bool IsTrailerBlock(byte block) => block % 4 == 3;

    private void CloseInternal()
    {
        if (!_handle.HasValue)
            return;

        try
        {
            Acr120Native.Close(_handle.Value);
        }
        finally
        {
            _handle = null;
            _readerPort = null;
        }
    }

    private short RequireHandle() => _handle ?? throw new InvalidOperationException("Leitor não está aberto.");

    private static byte[] ParseHex(string? value, int expectedBytes, string field)
    {
        var normalized = (value ?? string.Empty)
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace(":", string.Empty);

        if (normalized.Length != expectedBytes * 2)
            throw new ArgumentException($"{field} deve conter exatamente {expectedBytes} bytes ({expectedBytes * 2} caracteres hexadecimais).");

        try
        {
            return Convert.FromHexString(normalized);
        }
        catch (FormatException)
        {
            throw new ArgumentException($"{field} contém caracteres hexadecimais inválidos.");
        }
    }

    private static void ThrowIfError(short rc, string operation)
    {
        if (rc < 0)
            throw new Acr120Exception(operation, rc);
    }

    private static void EnsureWindowsX86()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O driver legado do ACR120U deste projeto é Windows.");
        if (Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("O processo precisa executar em x86 para carregar as DLLs do BIS 5.7.");
    }
}

public sealed class Acr120Exception(string operation, short errorCode)
    : Exception($"{operation} falhou. Código ACR120: {errorCode}")
{
    public string Operation { get; } = operation;
    public short ErrorCode { get; } = errorCode;
}

public enum MifareKeyType : byte
{
    CustomA = 0xAA,
    CustomB = 0xBB,
    DefaultA = 0xAD,
    DefaultB = 0xBD,
    DefaultF = 0xFD,
    StoredA = 0xAF,
    StoredB = 0xBF
}

public sealed record ReaderOpenResult(bool Open, short Handle, short Port);
public sealed record CardIdentity(byte TagType, byte SerialLength, string UidHex);
public sealed record BlockReadResult(byte Block, string DataHex, bool IsTrailer);
public sealed record SectorDumpResult(byte Sector, IReadOnlyList<BlockReadResult> Blocks);
