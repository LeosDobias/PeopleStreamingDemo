
using System.Linq.Expressions;
using System.Text.Json;

internal class Program
{
    private static async Task Main(string[] args)
    {

        var baseUrl = "http://localhost:63685";
        await ReadPeopleStrem(baseUrl, "/api/people/stream-sync", "AB");
        await ReadPeopleStrem(baseUrl, "/api/people/stream-async", "AB");
        await ReadPeopleStrem(baseUrl, "/api/people/stream-pipewriter", "AB");

        Console.ReadKey();
    }

    private static async Task ReadPeopleStrem(string baseUrl, string endpoint, string pattern)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        int count = 0;
        Console.WriteLine($"Reading NDJSON from {baseUrl}{endpoint}?pattern={pattern}");
        try
        {
            using var http = new HttpClient();
            using var resp = await http.GetAsync($"{baseUrl}{endpoint}?pattern={Uri.EscapeDataString(pattern)}",
                HttpCompletionOption.ResponseHeadersRead);

            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            string? line;

            while ((line = await reader.ReadLineAsync()) is not null)
            {
                var person = JsonSerializer.Deserialize<Person>(line, options);
                if (person is not null)
                {
                    count++;
                    if (count <= 10)
                        Console.WriteLine($"{person.Id}: {person.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error after reading {count} records: {ex}");
        }

        Console.WriteLine($"Total streamed: {count}");
    }
}

public record Person(int Id, string Name);