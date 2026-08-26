using System.Runtime.InteropServices;
using BisApi.Hardware;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["BisApi:Url"] ?? "http://127.0.0.1:8765");
builder.Services.AddSingleton<Acr120Service>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (Acr120Service reader) => Results.Ok(new
{
    ok = true,
    service = "bis_api",
    os = RuntimeInformation.OSDescription,
    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    is64BitProcess = Environment.Is64BitProcess,
    dllPresent = reader.DllPresent,
    dllExpectedPath = reader.ExpectedDllPath,
    readerOpen = reader.IsOpen,
    readerPort = reader.ReaderPort,
    rawWritesEnabled = app.Configuration.GetValue("BisApi:EnableRawWrites", false),
    trailerWritesEnabled = app.Configuration.GetValue("BisApi:AllowTrailerWrites", false)
}));

app.MapGet("/api/reader/dll-version", Execute((Acr120Service reader) =>
    Results.Ok(new { version = reader.GetDllVersion() })));

app.MapPost("/api/reader/open", Execute((Acr120Service reader, OpenReaderRequest request) =>
{
    if (request.Port is < 0 or > 7)
        return Results.BadRequest(new { error = "Port deve ser 0-7 (USB1-USB8)." });

    return Results.Ok(reader.Open(request.Port));
}));

app.MapPost("/api/reader/close", Execute((Acr120Service reader) =>
{
    reader.Close();
    return Results.Ok(new { open = false });
}));

app.MapGet("/api/card/select", Execute((Acr120Service reader) =>
    Results.Ok(reader.SelectCard())));

app.MapPost("/api/card/login", Execute((Acr120Service reader, LoginRequest request) =>
{
    reader.Login(request.Sector, request.KeyType, request.KeyHex);
    return Results.Ok(new { authenticated = true, request.Sector, keyType = request.KeyType.ToString() });
}));

app.MapGet("/api/card/block/{block:byte}", Execute((Acr120Service reader, byte block) =>
    Results.Ok(reader.ReadBlock(block))));

app.MapPost("/api/card/dump-sector", Execute((Acr120Service reader, DumpSectorRequest request) =>
    Results.Ok(reader.DumpSector(request.Sector, request.KeyType, request.KeyHex))));

app.MapPost("/api/card/block/{block:byte}", Execute((Acr120Service reader, byte block, WriteBlockRequest request) =>
{
    var writesEnabled = app.Configuration.GetValue("BisApi:EnableRawWrites", false);
    if (!writesEnabled)
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var requiredChallenge = app.Configuration["BisApi:RequireWriteChallenge"] ?? "GRAVAR";
    if (!string.Equals(request.Confirmation, requiredChallenge, StringComparison.Ordinal))
        return Results.BadRequest(new { error = "Confirmação de escrita inválida." });

    var trailerWrite = Acr120Service.IsTrailerBlock(block);
    var trailerWritesEnabled = app.Configuration.GetValue("BisApi:AllowTrailerWrites", false);
    if (trailerWrite && !trailerWritesEnabled)
        return Results.BadRequest(new { error = "Escrita em sector trailer está bloqueada por segurança." });

    reader.WriteBlock(block, request.DataHex);
    return Results.Ok(new { written = true, block, trailer = trailerWrite });
}));

app.MapPost("/api/hotel-card/encode", (HotelCardRequest request) =>
    Results.Json(new
    {
        implemented = false,
        message = "A camada física está pronta. O codec Saga/BIS 5.7 ainda precisa ser mapeado antes de gravar cartões de fechadura com segurança.",
        request
    }, statusCode: StatusCodes.Status501NotImplemented));

app.MapFallbackToFile("index.html");
app.Run();

static Delegate Execute(Func<Acr120Service, IResult> action) =>
    (Acr120Service reader) => Safe(() => action(reader));

static Delegate Execute<T1>(Func<Acr120Service, T1, IResult> action) =>
    (Acr120Service reader, T1 arg1) => Safe(() => action(reader, arg1));

static Delegate Execute<T1, T2>(Func<Acr120Service, T1, T2, IResult> action) =>
    (Acr120Service reader, T1 arg1, T2 arg2) => Safe(() => action(reader, arg1, arg2));

static IResult Safe(Func<IResult> action)
{
    try
    {
        return action();
    }
    catch (DllNotFoundException ex)
    {
        return Results.Json(new { error = "acr120u.dll não encontrada ou uma dependência está ausente.", detail = ex.Message }, statusCode: 500);
    }
    catch (BadImageFormatException ex)
    {
        return Results.Json(new { error = "Arquitetura incompatível. Execute o bis_api em x86.", detail = ex.Message }, statusCode: 500);
    }
    catch (Acr120Exception ex)
    {
        return Results.Json(new { error = ex.Message, operation = ex.Operation, code = ex.ErrorCode }, statusCode: 502);
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
public sealed record HotelCardRequest(string Room, DateTimeOffset ValidFrom, DateTimeOffset ValidUntil, string? GuestName = null);
