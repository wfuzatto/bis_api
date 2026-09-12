using System.Globalization;
using BisApi.Hardware;

namespace BisApi.Vendor;

public sealed class BeTech57Service
{
    private readonly IConfiguration _configuration;
    private readonly PcscService _pcsc;
    private readonly object _sync = new();

    public BeTech57Service(IConfiguration configuration, PcscService pcsc)
    {
        _configuration = configuration;
        _pcsc = pcsc;
    }

    public string CodecDllPath => Path.Combine(AppContext.BaseDirectory, "btlock57L.dll");
    public string ShimDllPath => Path.Combine(AppContext.BaseDirectory, "AcsReader.dll");
    public bool CodecPresent => File.Exists(CodecDllPath);
    public bool ShimPresent => File.Exists(ShimDllPath);

    public BeTech57Status Status()
    {
        var section = _configuration.GetSection("BeTech57");
        var password = section["HotelPassword"] ?? string.Empty;
        return new BeTech57Status(
            CodecPresent,
            ShimPresent,
            _configuration.GetValue("BisApi:EnableHotelCardWrites", false),
            password.Length == 6,
            section.GetValue("ReaderModel", 4),
            section.GetValue("Port", 1),
            section.GetValue("SectorNo", 0),
            section["PcscReader"] ?? string.Empty,
            "yyMMddHHmm");
    }

    public int SerialNoFromNow()
    {
        EnsureRuntime();
        return BeTech57Native.SerialNoFromNow();
    }

    public ReadSnrResult ReadSnr()
    {
        lock (_sync)
        {
            EnsureRuntime();
            var section = _configuration.GetSection("BeTech57");
            var card = _pcsc.Probe(section["PcscReader"]);
            ConfigureShim(card.Reader);
            var port = checked((byte)section.GetValue("Port", 1));
            var readerModel = checked((byte)section.GetValue("ReaderModel", 4));
            var serial = new byte[8];
            var result = BeTech57Native.ReadSnr(port, readerModel, serial);
            return new ReadSnrResult(result == 0, result, card.Reader,
                result == 0 ? System.Text.Encoding.ASCII.GetString(serial).TrimEnd('\0') : null, card.UidHex);
        }
    }

    private void ConfigureShim(string reader)
    {
        Environment.SetEnvironmentVariable("BIS_API_PCSC_READER", reader, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("BIS_API_SHIM_TRACE",
            _configuration.GetValue("BeTech57:ShimTrace", false) ? "1" : null,
            EnvironmentVariableTarget.Process);
    }

    public HotelCardWriteResult WriteGuestCard(HotelCardWriteRequest request)
    {
        lock (_sync)
        {
            EnsureRuntime();
            if (!_configuration.GetValue("BisApi:EnableHotelCardWrites", false))
                throw new InvalidOperationException("A emissão de cartão está desabilitada. Defina BisApi:EnableHotelCardWrites=true no appsettings.Local.json somente no computador autorizado.");

            var challenge = _configuration["BisApi:RequireWriteChallenge"] ?? "GRAVAR";
            if (!string.Equals(request.Confirmation, challenge, StringComparison.Ordinal))
                throw new ArgumentException("Confirmação de gravação inválida.", nameof(request.Confirmation));

            if (request.ValidUntil <= request.ValidFrom)
                throw new ArgumentException("A data/hora final precisa ser posterior à inicial.");

            var section = _configuration.GetSection("BeTech57");
            var password = (section["HotelPassword"] ?? string.Empty).Trim();
            if (password.Length != 6 || !password.All(char.IsDigit))
                throw new InvalidOperationException("Configure BeTech57:HotelPassword com os 6 dígitos da senha HPASS do PMS Saga. Não versione essa senha no Git.");

            var readerName = request.ReaderName;
            if (string.IsNullOrWhiteSpace(readerName))
                readerName = section["PcscReader"];

            // Fail-fast: confirma que há um ACR122/cartão presente antes de entrar no codec legado.
            var card = _pcsc.Probe(readerName);
            ConfigureShim(card.Reader);
            ApplyOptionalKeyEnvironment(section);

            var readerModel = checked((byte)section.GetValue("ReaderModel", 4)); // Codec selector 4 = ACSMF1USB / AcsReader.dll.
            var port = checked((byte)section.GetValue("Port", 1));
            var sector = checked((byte)section.GetValue("SectorNo", 0));

            var doorId = NormalizeDoor(request.RoomOrDoorId);
            var suitDoor = NormalizeHexMask(request.SuitDoor, 12, "SuitDoor", "000000000000");
            var publicDoor = NormalizeHexMask(request.PublicDoor, 8, "PublicDoor", "00000000");
            var guestIndex = request.GuestIndex is > 0 and <= 255 ? request.GuestIndex.Value : 2;
            var guestSerial = request.GuestSerial.GetValueOrDefault();
            if (guestSerial <= 0)
                guestSerial = BeTech57Native.SerialNoFromNow();
            var holderSerial = request.HolderSerial.GetValueOrDefault();

            // The BIS PMS card format is YYMMDDHHmm (10 ASCII characters).
            var begin = request.ValidFrom.ToLocalTime().ToString("yyMMddHHmm", CultureInfo.InvariantCulture);
            var end = request.ValidUntil.ToLocalTime().ToString("yyMMddHHmm", CultureInfo.InvariantCulture);

            var result = BeTech57Native.WriteGuestCard(
                port,
                readerModel,
                sector,
                password,
                guestSerial,
                holderSerial,
                guestIndex,
                doorId,
                suitDoor,
                publicDoor,
                begin,
                end);

            return new HotelCardWriteResult(
                result == 0,
                result,
                DescribeResult(result),
                card.Reader,
                card.UidHex,
                doorId,
                begin,
                end,
                guestSerial,
                holderSerial,
                guestIndex,
                suitDoor,
                publicDoor);
        }
    }

    private void EnsureRuntime()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O codec Be-Tech 5.7L é Windows x86.");
        if (Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("O processo precisa executar em x86 para carregar btlock57L.dll.");
        if (!CodecPresent)
            throw new DllNotFoundException($"btlock57L.dll não encontrada em {CodecDllPath}");
        if (!ShimPresent)
            throw new DllNotFoundException($"AcsReader.dll (shim PC/SC) não encontrada em {ShimDllPath}");
    }

    private static string NormalizeDoor(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new ArgumentException("Informe o quarto/door_id.");
        if (text.Length > 6)
            throw new ArgumentException("DoorID deve ter no máximo 6 caracteres.");
        if (text.All(char.IsDigit))
            return text.PadLeft(6, '0');
        if (text.Length != 6)
            throw new ArgumentException("DoorID alfanumérico deve ter exatamente 6 caracteres.");
        return text;
    }

    private static string NormalizeHexMask(string? value, int length, string field, string defaultValue)
    {
        var text = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToUpperInvariant();
        if (text.Length != length || !text.All(Uri.IsHexDigit))
            throw new ArgumentException($"{field} deve conter exatamente {length} caracteres hexadecimais.");
        return text;
    }

    private static void ApplyOptionalKeyEnvironment(IConfiguration section)
    {
        CopySecret("DefaultKeyA", "BIS_API_DEFAULT_KEY_A");
        CopySecret("DefaultKeyB", "BIS_API_DEFAULT_KEY_B");
        CopySecret("StoredKeyA", "BIS_API_STORED_KEY_A");
        CopySecret("StoredKeyB", "BIS_API_STORED_KEY_B");

        void CopySecret(string configName, string envName)
        {
            var value = section[configName];
            if (!string.IsNullOrWhiteSpace(value))
                Environment.SetEnvironmentVariable(envName, value.Trim(), EnvironmentVariableTarget.Process);
        }
    }

    private static string DescribeResult(int result) => result switch
    {
        0 => "Sucesso",
        1 => "Falha ao abrir o leitor/porta",
        2 => "Nenhum cartão detectado",
        3 => "Tipo de cartão incompatível",
        4 => "Falha de leitura/autenticação",
        5 => "Senha/código do hotel rejeitado",
        6 => "Falha de gravação",
        255 => "Falha genérica do codec Be-Tech",
        _ when result < 0 => $"Falha do backend de leitor ({result})",
        _ => $"Código retornado pelo codec: {result}"
    };
}

public sealed record ReadSnrResult(bool Success, int VendorResult, string Reader, string? Serial, string UidHex);

public sealed record BeTech57Status(
    bool CodecPresent,
    bool PcscShimPresent,
    bool HotelCardWritesEnabled,
    bool HotelPasswordConfigured,
    int ReaderModel,
    int Port,
    int SectorNo,
    string PcscReader,
    string DateTimeFormat);

public sealed record HotelCardWriteRequest(
    string RoomOrDoorId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    string Confirmation,
    string? ReaderName = null,
    int? GuestSerial = null,
    int? HolderSerial = null,
    int? GuestIndex = null,
    string? SuitDoor = null,
    string? PublicDoor = null,
    string? GuestName = null);

public sealed record HotelCardWriteResult(
    bool Written,
    int VendorResult,
    string Message,
    string Reader,
    string UidHex,
    string DoorId,
    string BeginTime,
    string EndTime,
    int GuestSerial,
    int HolderSerial,
    int GuestIndex,
    string SuitDoor,
    string PublicDoor);
