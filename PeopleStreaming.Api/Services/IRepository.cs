namespace PeopleStreaming.Api.Services
{
    public interface IRepository
    {
        IAsyncEnumerable<Person> StreamPeopleAsync(string pattern, CancellationToken ct = default);
        IEnumerable<Person> StreamPeopleSync(string pattern, CancellationToken ct = default);
    }
}