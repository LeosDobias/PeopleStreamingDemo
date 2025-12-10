using Microsoft.AspNetCore.Mvc;
using PeopleStreaming.Api.Services;
using System.Buffers;
using System.Text;

namespace PeopleStreaming.Api.Endpoints;

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/people")
                       .WithTags("People")
                       .WithGroupName("People"); // pro swagger grouping

        group.MapGet("/getarray", GetPeopleArray)
            .WithName("GetPeopleArray")
            .Produces<List<Person>>(StatusCodes.Status200OK);

        group.MapGet("/stream-sync", StreamPeopleNdjsonSinc)
            .WithName("StreamPeopleNdjsonSinc")
            .Produces<string>(StatusCodes.Status200OK, "application/x-ndjson");

        group.MapGet("/stream-async", StreamPeopleNdjsonAsync)
            .WithName("StreamPeopleNdjsonAsync")
            .Produces<string>(StatusCodes.Status200OK, "application/x-ndjson");

        group.MapGet("/stream-pipewriter", StreamPeoplePipeWriterAsync)
            .WithName("StreamPeoplePipeWriterAsync")
            .Produces<string>(StatusCodes.Status200OK, "application/x-ndjson");

        return app;
    }

    private static IResult GetPeopleArray(
        [FromQuery] string pattern, IRepository repo, CancellationToken ct)
    {
        var people = new List<Person>(capacity: 1024);
        foreach (var p in repo.StreamPeopleSync(pattern))
        {
            people.Add(p);
        }
        return Results.Ok(people);
    }

    private static async Task StreamPeopleNdjsonSinc(
    HttpContext ctx, [FromQuery] string pattern, IRepository repo, CancellationToken ct)
    {
        ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";
        foreach (var p in repo.StreamPeopleSync(pattern, ct))
        {
            ct.ThrowIfCancellationRequested();
            await ctx.Response.WriteAsync(Ndjson.SerializeToNdjson(p), ct);
        }
    }

    private static async Task StreamPeopleNdjsonAsync(
        HttpContext ctx, [FromQuery] string pattern, IRepository repo, CancellationToken ct)
    {
        ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";

        // Nastartuj odpověď: hlavičky odejdou ihned
        await ctx.Response.StartAsync(ct);

        await foreach (var p in repo.StreamPeopleAsync(pattern, ct))
            await ctx.Response.WriteAsync(Ndjson.SerializeToNdjson(p), ct);
    }


    /*
     * StreamPeoplePipeWriterAsync
     * ---------------------------
     * Tento handler zapisuje NDJSON do HTTP response pomocí HttpResponse.BodyWriter,
     * tedy přes API System.IO.Pipelines (PipeWriter). Je to *jen jiná forma zápisu*
     * do stejného response streamu než HttpResponse.WriteAsync — výsledkem je stále
     * totéž: bajty odchází klientovi po síti.
     *
     * Co je jinak oproti Response.WriteAsync:
     *  - BodyWriter (PipeWriter) dává jemnější kontrolu nad bufferováním a flushováním.
     *    Samotné writer.Write(...) zapisuje do interního bufferu; až FlushAsync()
     *    data zpřístupní transportu (Kestrel) a mohou jít ven. U Response.WriteAsync
     *    řeší heuristiky flushování framework sám.
     *  - Lepší výkon/nižší alokace ve scénářích s častými a malými zápisy (streamování
     *    po řádcích). PipeWriter umí poskytovat přímo paměťové bloky (GetMemory/Advance),
     *    takže se dá minimalizovat kopírování a alokace.
     *  - Stejný cancellation model: CancellationToken (RequestAborted) je stále signál
     *    k ukončení práce. U pipelines ho předáváme do FlushAsync/WriteAsync (případně
     *    kontrolujeme ct.ThrowIfCancellationRequested();), u Response.WriteAsync ho
     *    předáváme do samotného zápisu.
     *
     * Co to znamená prakticky:
     *  - Funkčně je to ekvivalentní ke streamování pomocí Response.WriteAsync; mění se
     *    jen API, ne sémantika výsledku. Nicméně s PipeWriterem explicitně rozhodujeme,
     *    kdy posílat data (FlushAsync po každé položce nebo po dávkách 16–64 kusů).
     *  - Pro „živé“ NDJSON streamy preferujeme FlushAsync ihned po položce (nejnižší latence).
     *    Pokud je prioritou propustnost, flushujeme po dávkách a snížíme režii syscalls.
     *  - Všechny hlavičky (Content-Type, status) nastavujeme *před* případným StartAsync;
     *    po odeslání hlaviček je už nelze bezpečně měnit.
     *
     * Shrnutí:
     *  - PipeWriter = výkonnější a jemněji ovladatelná alternativa zápisu do response streamu.
     *  - Response.WriteAsync = jednodušší API, kde flush řeší framework.
     *  - V obou případech jde pořád o zápis do téže HTTP odpovědi; volba API závisí na
     *    požadavcích na latenci/propustnost a granularitu řízení bufferů.
     */

    private static async Task StreamPeoplePipeWriterAsync(
        HttpContext ctx, [FromQuery] string pattern, IRepository repo, IConfiguration cfg, CancellationToken ct)
    {
        ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";

        // Nastartuj odpověď: hlavičky odejdou ihned
        await ctx.Response.StartAsync(ct);

        var writer = ctx.Response.BodyWriter;

        int batchSize = cfg.GetValue<int>("Streaming:BatchSize", 32);  // doporuceno 16–64, pripadne 1 pro flush pro kazdou polozku
        int inBatch = 0;

        await foreach (var person in repo.StreamPeopleAsync(pattern, ct))
        {
            ct.ThrowIfCancellationRequested();

            var ndjson = Ndjson.SerializeToNdjson(person);
            writer.Write(Encoding.UTF8.GetBytes(ndjson));

            if (++inBatch >= batchSize)
            {
                var flush = await writer.FlushAsync(ct);
                if (flush.IsCompleted) break; // klient ukončil spojení
                inBatch = 0;
            }
        }

        // doflushování zbytku nedokončené dávky
        await writer.FlushAsync(ct);
    }


    /*
     * DI přes parametry v Minimal API:
     * - Funguje tam, kde ASP.NET Core dělá parameter binding: hlavně u route handlerů (MapGet/MapPost/…),
     *   a také u Minimal API filtrů. Parametry se plní z DI, z route/query/header nebo z body. [viz MS Learn]
     * - Mimo tyto „entry points“ (běžné metody tříd, middleware) použij klasickou konstruktorovou DI
     *   nebo IServiceProvider.GetService<T>() pro volitelné služby.
     * - Pro handler lze injektovat např. IConfiguration, ILogger<T>, IOptions<T>, DbContext, vlastní služby.
     * - U lambd nelze mít defaultní hodnoty parametrů; pro „volitelné“ hodnoty použij nullable typy
     *   nebo mapuj na handler metodu jake jsou pouzity zde, kde defaulty povolené jsou.
     */



}
