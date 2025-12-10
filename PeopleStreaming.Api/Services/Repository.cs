
using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace PeopleStreaming.Api.Services;

public class DbRepository : IRepository
{
    private readonly string _cs;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbRepository> _logger;
    private const string sql = @"SELECT Id, Name FROM dbo.Person WHERE Name LIKE '%' + @pattern + '%' ORDER BY Id";


    public DbRepository(IConfiguration configuration, ILogger<DbRepository> logger)
    {
        _configuration = configuration;
        _cs = _configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Connection string 'Default' not found.");
        _logger = logger;
    }

    // Vrací IAsyncEnumerable<Person> – asynchronní stream dat z DB.
    // Doporuèeno pro pouití v Minimal API
    // Klíèové vlastnosti:
    // - Lazy loading: data se naèítají postupnì pøi iteraci (await foreach).
    // - Materializace celé kolekce najednou nastane a pøi pouití jako ToListAsync(), ToArrayAsync() z nugetu System.Linq.Async.
    // - Neblokuje hlavní vlákno: vyuívá async/await pro I/O operace.
    // - Umoòuje efektivní streamování velkıch datasetù.
    // Pouití: await foreach (var person in repo.StreamPeopleAsync(pattern, ct)) { ... }
    public async IAsyncEnumerable<Person> StreamPeopleAsync(string pattern, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@pattern", SqlDbType.NVarChar, 200) { Value = pattern });

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            yield return new Person(id, name);
        }
    }

    // Vrací IEnumerable<Person> – synchronní stream dat z DB.
    // Klíèové vlastnosti:
    // - Lazy loading: ètení z DB probíhá a pøi iteraci (foreach).
    // - Pamìová efektivita: pøi foreach iteraci drí v pamìti pouze aktuální záznam.
    // - Materializace celé kolekce najednou nastane a pøi pouití LINQ metod jako ToList(), ToArray().
    // Pouití: foreach (var person in repo.StreamPeopleSync(pattern, ct)) { ... }
    public IEnumerable<Person> StreamPeopleSync(string pattern, CancellationToken ct = default)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@pattern", SqlDbType.NVarChar, 200) { Value = pattern });

        // Registrace na token: pøi zrušení zavolá cmd.Cancel()
        using var reg = ct.Register(() =>
        {
            try { cmd.Cancel(); } catch { /* best-effort: potlaèit */ }
        });

        using var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess); // SequentialAccess zlepsi vykon pro streamy

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();

            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            yield return new Person(id, name);
        }
    }

    // Vrací IEnumerable<Person> – synchronní stream dat z DB.
    // Klíèové vlastnosti:
    // - Lazy loading: ètení z DB probíhá a pøi iteraci (foreach).
    // - Pamìová efektivita: pøi foreach iteraci drí v pamìti pouze aktuální záznam.
    // - Materializace celé kolekce najednou nastane a pøi pouití LINQ metod jako ToList(), ToArray().
    // Pouití: foreach (var person in repo.StreamPeopleSync(pattern)) { ... }
    public IEnumerable<Person> StreamPeopleSync_old(string pattern)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@pattern", SqlDbType.NVarChar, 200) { Value = pattern });

        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            count++;
            yield return new Person(id, name);
        }
        _logger.LogInformation("StreamPeopleSync: Retrieved {Count} records for pattern '{Pattern}'", count, pattern);
    }

    // Tato verze IEnumerable vraci jiz zmaterializovanou kolekci
    public IEnumerable<Person> GetPeople(string pattern)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();

        const string sql = @"SELECT Id, Name FROM dbo.Person WHERE Name LIKE '%' + @pattern + '%' ORDER BY Id";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@pattern", SqlDbType.NVarChar, 200) { Value = pattern });

        var res = new List<Person>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            res.Add(new Person(id, name));
        }
        return res;
    }


}