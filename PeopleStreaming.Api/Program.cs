using Azure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PeopleStreaming.Api;

internal class Program
{
    private static void Main(string[] args)
    {
        #region API setings
        Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .CreateBootstrapLogger();
        try
        {
            Log.Information("Initialization of Minimal API {Application}", MethodBase.GetCurrentMethod()?.DeclaringType?.Namespace);
            var builder = WebApplication.CreateBuilder(args);

            // Plná konfigurace Serilogu z appsettings.json
            builder.Host.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());

            // Add Swagger (for JSON array endpoint mainly)
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Enable static files (to serve openapi.yaml and openapi.html)
            builder.Services.AddRouting();

            // Ad-hoc reseni, Repository by mala byt injektovana sluzba!
            Repository._cs = builder.Configuration.GetConnectionString("Default");

            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            #endregion

            app.MapGet("/", () => Results.Redirect("/openapi.html"));

            #region Define 'People' endpoints - right

            // 1) Definuj lokální metodu/handler pro "/api/people/stream-async"
            static async Task StreamPeopleNdjsonAsync(HttpContext ctx, [FromQuery] string pattern, CancellationToken ct)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("Query parameter 'pattern' is required.", ct);
                    return;
                }

                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";


                // verze pro pro asynchronni zdroj dat IAsyncEnumerable <Preson> - doporuceno pro rest API !!!
                await foreach (var person in Repository.StreamPeopleAsync(pattern, ct))
                {
                    var ndjson = Ndjson.SerializeToNdjson(person);
                    await ctx.Response.WriteAsync(ndjson, ct);
                }

                //verze pro pro synchronni zdroj dat IEnumerale<Preson>
                //foreach (var person in Repository.StreamPeopleSync(pattern))
                //{
                //    ct.ThrowIfCancellationRequested();
                //    var ndjson = Ndjson.SerializeToNdjson(person);
                //    await ctx.Response.WriteAsync(ndjson, ct);
                //}


                // žádný návrat IResult – stream už je zapsaný
            }

            // 2) namapovani handleru StreamPeopleNdjsonAsync
            app.MapGet("/api/people/stream-async", StreamPeopleNdjsonAsync)
               .WithName("StreamPeopleNdjsonAsync")
               .Produces<string>(StatusCodes.Status200OK, contentType: "application/x-ndjson");
               //.WithOpenApi();

            #endregion

            #region Define 'People' endpoints - wrong/broken stream

            app.MapGet("/api/people/stream-async-error", async ([FromQuery] string pattern, HttpContext ctx, CancellationToken ct) =>
            {

                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.ContentType = "application/x-ndjson; charset=utf-8";

                var connStr = ctx.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                             ?? throw new InvalidOperationException("Missing ConnectionStrings:Default in appsettings.json");

                try
                {
                    await foreach (var person in Repository.StreamPeopleAsync(pattern, ct))
                    {
                        var ndjson = Ndjson.SerializeToNdjson(person);
                        await ctx.Response.WriteAsync(ndjson, ct);
                    }

                    // Finish
                    return StatusCodes.Status200OK; // nastaveni statusu po zapsani stremu zpusobi pad streamu u klienta
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Exception during processing StreamPeopleNdjsonAsync");
                    throw;
                }


            })
            .WithName("StreamPeopleNdjsonAsyncWithError")
            .Produces<string>(StatusCodes.Status200OK, contentType: "application/x-ndjson");

            #endregion



            app.MapGet("/api/people", async ([FromQuery] string pattern, HttpContext ctx, CancellationToken ct) =>
            {
                var people = new List<Person>(capacity: 1024);
                await foreach (var p in Repository.StreamPeopleAsync(pattern, ct))
                {
                    people.Add(p);
                }

                return Results.Ok(people);
            })
            .WithName("GetPeopleAsArray").WithOpenApi();

            // Po spuštění aplikace zaloguj adresy
            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var server = app.Services.GetRequiredService<IServer>();
                var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;

                if (addresses != null)
                {
                    foreach (var address in addresses)
                    {
                        Log.Information("Minimal API {Application} has been started on address: {addr}", MethodBase.GetCurrentMethod()?.DeclaringType?.Namespace, address);
                    }
                }
                // Vypiš vše z endpoint routingu
                var sources = app.Services.GetServices<EndpointDataSource>();
                foreach (var src in sources)
                {
                    foreach (var endpoint in src.Endpoints)
                    {
                        if (endpoint is RouteEndpoint re)
                            Log.Information("Listening endpoint: {Route}", re.RoutePattern.RawText);
                    }
                }
            });

            app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed on start!");
        }
        finally
        {
            Log.Information("Application is closing.");
            Log.CloseAndFlush();
        }
    }

    static class Ndjson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static string SerializeToNdjson<T>(T item)
        {
            var json = JsonSerializer.Serialize(item, Options);
            return json + "\n";
        }
    }
}
public record Person(int Id, string Name);