using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using BisApi.Hardware;
using BisApi.Vendor;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Host.UseWindowsService(options => options.ServiceName = "BisApi");
builder.WebHost.UseUrls(builder.Configuration["BisApi:Url"] ?? "http://127.0.0.1:8765");

builder.Services.AddSingleton<Acr120Service>();
builder.Services.AddSingleton<PcscService>();
builder.Services.AddSingleton<BeTech57Service>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (Acr120Service legacy, BeTech57Service vendor) => Safe(() => Results.Ok(new
{
    ok = true,
    service = "bis_api",
    mode = "standalone",
    os = RuntimeInformation.OSDescription,
    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    is64BitProcess = Environment.Is64BitProcess,
    url = app.Configuration["BisApi:Url"] ?? "http://127.0.0.1:8765",
    pcsc = "WinSCard",
    vendor = vendor.Status(),
    legacyAcr120 = new
    {
        dllPresent = legacy.DllPresent,
        readerOpen = legacy.IsOpen,
        readerPort = legacy.ReaderPort
    },
    rawWritesEnabled = app.Configuration.GetValue("BisApi:EnableRawWrites", false),
    trailerWritesEnabled = app.Configuration.GetValue("BisApi:AllowTrailerWrites", false)
})));

// ACR122 / PC-SC
app.MapGet("/api/pcsc/readers", (PcscService pcsc) => Safe(() => Results.Ok(new { readers = pcsc.ListReaders() })));
app.MapGet("/api/pcsc/probe", (PcscService pcsc, string? reader) => Safe(() => Results.Ok(pcsc.Probe(reader))));

// Codec original Be-Tech/Saga + shim PC/SC
app.MapGet("/api/vendor/status", (BeTech57Service vendor) => Safe(() => Results.Ok(vendor.Status())));
app.MapGet("/api/vendor/read-snr", (BeTech57Service vendor) => Safe(() => Results.Ok(vendor.ReadSnr())));
app.MapGet("/api/vendor/serial", (BeTech57Service vendor) => Safe(() => Results.Ok(new { serial = vendor.SerialNoFromNow() })));
app.MapPost("/api/hotel-card/encode", (BeTech57Service vendor, HotelCardWriteRequest request) =>
    Safe(() =>
    {
        var result = vendor.WriteGuestCard(request);
        return result.Written
            ? Results.Ok(result)
            : Results.Json(result, statusCode: StatusCodes.Status502BadGateway);
    }));

// Backend legado ACR120/RW-41 mantido para diagnóstico e rollback.
app.MapGet("/api/reader/dll-version", (Acr120Service reader) => Safe(() =>
    Results.Ok(new { version = reader.GetDllVersion() })));

app.MapPost("/api/reader/open", (Acr120Service reader, OpenReaderRequest request) => Safe(() =>
{
    if (request.Port is < 0 or > 7)
        return Results.BadRequest(new { error = "Port deve ser 0-7 (USB1-USB8)." });
    return Results.Ok(reader.Open(request.Port));
}));

app.MapPost("/api/reader/close", (Acr120Service reader) => Safe(() =>
{
    reader.Close();
    return Results.Ok(new { open = false });
}));

app.MapGet("/api/card/select", (Acr120Service reader) => Safe(() => Results.Ok(reader.SelectCard())));

app.MapPost("/api/card/login", (Acr120Service reader, LoginRequest request) => Safe(() =>
{
    reader.Login(request.Sector, request.KeyType, request.KeyHex);
    return Results.Ok(new { authenticated = true, request.Sector, keyType = request.KeyType.ToString() });
}));

app.MapGet("/api/card/block/{block:int}", (Acr120Service reader, int block) => Safe(() =>
{
    if (block is < 0 or > 255)
        return Results.BadRequest(new { error = "Bloco deve estar entre 0 e 255." });
    return Results.Ok(reader.ReadBlock((byte)block));
}));

app.MapPost("/api/card/dump-sector", (Acr120Service reader, DumpSectorRequest request) => Safe(() =>
    Results.Ok(reader.DumpSector(request.Sector, request.KeyType, request.KeyHex))));

app.MapPost("/api/card/block/{block:int}", (Acr120Service reader, int block, WriteBlockRequest request) => Safe(() =>
{
    if (block is < 0 or > 255)
        return Results.BadRequest(new { error = "Bloco deve estar entre 0 e 255." });

    var blockByte = (byte)block;
    if (!app.Configuration.GetValue("BisApi:EnableRawWrites", false))
        return Results.Json(new { error = "Escrita crua está desabilitada em appsettings.Local.json." }, statusCode: StatusCodes.Status403Forbidden);

    var requiredChallenge = app.Configuration["BisApi:RequireWriteChallenge"] ?? "GRAVAR";
    if (!string.Equals(request.Confirmation, requiredChallenge, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "Confirmação de escrita inválida." });

    var trailerWrite = Acr120Service.IsTrailerBlock(blockByte);
    if (trailerWrite && !app.Configuration.GetValue("BisApi:AllowTrailerWrites", false))
        return Results.BadRequest(new { error = "Escrita em sector trailer está bloqueada por segurança." });

    reader.WriteBlock(blockByte, request.DataHex);
    return Results.Ok(new { written = true, block, trailer = trailerWrite });
}));

app.MapFallbackToFile("index.html");
app.Run();

static IResult Safe(Func<IResult> action)
{
    try
    {
        return action();
    }
    catch (DllNotFoundException ex)
    {
        return Results.Json(new { error = "DLL necessária não encontrada.", detail = ex.Message }, statusCode: 500);
    }
    catch (EntryPointNotFoundException ex)
    {
        return Results.Json(new { error = "A DLL encontrada não possui a função esperada.", detail = ex.Message }, statusCode: 500);
    }
    catch (BadImageFormatException ex)
    {
        return Results.Json(new { error = "Arquitetura incompatível. Execute o bis_api em x86.", detail = ex.Message }, statusCode: 500);
    }
    catch (Acr120Exception ex)
    {
        return Results.Json(new { error = ex.Message, operation = ex.Operation, code = ex.ErrorCode }, statusCode: 502);
    }
    catch (PcscException ex)
    {
        return Results.Json(new
        {
            error = ex.Message,
            operation = ex.Operation,
            code = $"0x{unchecked((uint)ex.ErrorCode):X8}"
        }, statusCode: 502);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or PlatformNotSupportedException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

public sealed record OpenReaderRequest(short Port = 0);
public sealed record LoginRequest(byte Sector, MifareKeyType KeyType, string? KeyHex = null);
public sealed record DumpSectorRequest(byte Sector, MifareKeyType KeyType, string? KeyHex = null);
public sealed record WriteBlockRequest(string DataHex, string Confirmation);
