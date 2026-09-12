using BisApi.Hardware;

namespace BisApi.Vendor;

/// <summary>
/// Diagnóstico estritamente somente leitura para diferenciar uma pulseira que
/// aceita a chave do hotel de uma pulseira ainda na chave padrão do fabricante.
/// Nenhum sector trailer ou bloco é escrito por este serviço.
/// </summary>
public sealed class GuestCardDiagnosticsService
{
    private readonly IConfiguration _configuration;
    private readonly PcscService _pcsc;
    private readonly object _sync = new();

    public GuestCardDiagnosticsService(IConfiguration configuration, PcscService pcsc)
    {
        _configuration = configuration;
        _pcsc = pcsc;
    }

    public GuestCardKeyStateResult Inspect()
    {
        lock (_sync)
        {
            EnsureRuntime();

            var section = _configuration.GetSection("BeTech57");
            var password = (section["HotelPassword"] ?? string.Empty).Trim();
            if (password.Length != 6 || !password.All(char.IsDigit))
                throw new InvalidOperationException("HotelPassword/HPASS não está configurado corretamente.");

            var card = _pcsc.Probe(section["PcscReader"]);
            Environment.SetEnvironmentVariable("BIS_API_PCSC_READER", card.Reader, EnvironmentVariableTarget.Process);
            ApplyOptionalKeyEnvironment(section);

            var port = checked((byte)section.GetValue("Port", 1));
            var readerModel = checked((byte)section.GetValue("ReaderModel", 4));
            var sector = checked((byte)section.GetValue("SectorNo", 0));

            // O codec retorna 5 especificamente quando a autenticação com a
            // senha/código do hotel falha. Resultados 0 e 4 significam que ele
            // ultrapassou essa validação; 4 pode ocorrer em cartão ainda sem um
            // registro de hóspede válido.
            var hotelResult = ReadGuestCardResultOnly(port, readerModel, sector, password);
            if (hotelResult is 0 or 4)
            {
                return new GuestCardKeyStateResult(
                    true,
                    hotelResult == 0 ? "hotel_key_guest_card" : "hotel_key_accepted",
                    hotelResult,
                    null,
                    card.Reader,
                    card.UidHex,
                    hotelResult == 0
                        ? "A pulseira autentica com o código deste hotel e contém um cartão de hóspede legível."
                        : "A pulseira autentica com o código deste hotel, mas ainda não contém um cartão de hóspede válido.");
            }

            if (hotelResult != 5)
            {
                return new GuestCardKeyStateResult(
                    false,
                    "inconclusive",
                    hotelResult,
                    null,
                    card.Reader,
                    card.UidHex,
                    $"O codec não chegou a uma conclusão sobre a chave do cartão (resultado {hotelResult}).");
            }

            // No btlock57L.dll, senha vazia seleciona a chave padrão interna
            // 1AB23CD45EF6. Esta segunda leitura continua sendo somente leitura
            // e permite reconhecer cartão ainda não migrado para a chave do hotel.
            var factoryResult = ReadGuestCardResultOnly(port, readerModel, sector, string.Empty);
            if (factoryResult is 0 or 4)
            {
                return new GuestCardKeyStateResult(
                    true,
                    "factory_key_detected",
                    hotelResult,
                    factoryResult,
                    card.Reader,
                    card.UidHex,
                    "A pulseira ainda aceita a chave padrão do fabricante. Ela precisa ser provisionada pelo fluxo oficial do BIS antes da emissão pelo Totem.");
            }

            if (factoryResult == 5)
            {
                return new GuestCardKeyStateResult(
                    true,
                    "unknown_key_or_hpass_mismatch",
                    hotelResult,
                    factoryResult,
                    card.Reader,
                    card.UidHex,
                    "A pulseira não autentica com o código deste hotel nem com a chave padrão. Verifique se pertence a outro hotel ou se o HPASS configurado corresponde a esta instalação.");
            }

            return new GuestCardKeyStateResult(
                false,
                "inconclusive",
                hotelResult,
                factoryResult,
                card.Reader,
                card.UidHex,
                $"A leitura com a chave padrão também foi inconclusiva (resultado {factoryResult}).");
        }
    }

    private static int ReadGuestCardResultOnly(byte port, byte readerModel, byte sector, string password)
    {
        var doorId = new byte[7];
        var suitDoor = new byte[5];
        var publicDoor = new byte[9];
        var beginTime = new byte[11];
        var endTime = new byte[11];

        return BeTech57Native.ReadGuestCard(
            port,
            readerModel,
            sector,
            password,
            out _,
            out _,
            out _,
            doorId,
            suitDoor,
            publicDoor,
            beginTime,
            endTime);
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

    private static void EnsureRuntime()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O diagnóstico Be-Tech requer Windows.");
        if (Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("O processo precisa executar em x86 para carregar btlock57L.dll.");

        var codec = Path.Combine(AppContext.BaseDirectory, "btlock57L.dll");
        var shim = Path.Combine(AppContext.BaseDirectory, "AcsReader.dll");
        if (!File.Exists(codec))
            throw new DllNotFoundException($"btlock57L.dll não encontrada em {codec}");
        if (!File.Exists(shim))
            throw new DllNotFoundException($"AcsReader.dll não encontrada em {shim}");
    }
}

public sealed record GuestCardKeyStateResult(
    bool Conclusive,
    string State,
    int HotelVendorResult,
    int? FactoryVendorResult,
    string Reader,
    string UidHex,
    string Message);
