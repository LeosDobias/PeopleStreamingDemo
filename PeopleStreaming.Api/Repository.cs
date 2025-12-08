
using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;

namespace PeopleStreaming.Api;

static class Repository
{
    public static string _cs;

    public static async IAsyncEnumerable<Person> StreamPeopleAsync(string pattern, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var sql = $@"SELECT Id, Name FROM dbo.Person WHERE Name LIKE '%{pattern}%' ORDER BY Id";

        await using var cmd = new SqlCommand(sql, conn);

        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.Default, ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            yield return new Person(id, name);
        }
    }

    public static IEnumerable<Person> StreamPeopleSync(string pattern)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();

        const string sql = @"SELECT Id, Name FROM dbo.Person WHERE Name LIKE '%' + @pattern + '%' ORDER BY Id";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@pattern", SqlDbType.NVarChar, 200) { Value = pattern });

        using var reader = cmd.ExecuteReader(CommandBehavior.Default);
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            yield return new Person(id, name);
        }
    }
}