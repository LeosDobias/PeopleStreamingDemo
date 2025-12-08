
# PeopleStreamingDemo

Demo řeší REST API s NDJSON streamováním a klientem.

## Projekty
- **PeopleStreaming.Api** – ASP.NET Core Minimal API (NET 9), endpointy:
  - `GET /api/people?pattern=...` – vrací pole JSON (materializované)
  - `GET /api/people/stream?pattern=...` – NDJSON stream (IAsyncEnumerable)
  - `GET /api/people/stream-sync?pattern=...` – NDJSON stream (IEnumerable)
  - Swagger UI: `/swagger`
  - OpenAPI YAML: `/openapi.yaml`
  - Redoc (HTML): `/openapi.html`

- **PeopleStreaming.Client** – .NET 9 konzolová app, čte NDJSON po řádcích.

## Databáze
Používá se `PeopleDb` na `(localdb)\MSSQLLocalDB`. Vytvoř DB a data pomocí `sql/seed.sql`.

## Spuštění
1. Vytvoř databázi a naplň data (viz níže).
2. Otevři solution ve VS2022, nastav **PeopleStreaming.Api** jako startup, spusť.
3. Testuj:
   - `curl -N "http://localhost:5199/api/people/stream?pattern=AB"`
   - Nebo otevři `/openapi.html`.

## SQL seed
Viz soubor `PeopleStreaming.Api/sql/seed.sql`.
