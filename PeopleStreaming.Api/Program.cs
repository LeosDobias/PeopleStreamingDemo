using Azure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using PeopleStreaming.Api.Endpoints;
using PeopleStreaming.Api.Services;
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

            builder.Services.AddSingleton<IRepository, DbRepository>();

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

            app.MapPeopleEndpoints();

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
}
public record Person(int Id, string Name);


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