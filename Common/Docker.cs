using Testcontainers.PostgreSql;

namespace Common;

public static class Docker
{
    public static async Task<PostgreSqlContainer> StartPostGisContainer()
    {
        var postgreSqlContainer = new PostgreSqlBuilder().WithImage("postgis/postgis:12-3.0").Build();
        await postgreSqlContainer.StartAsync();
        return postgreSqlContainer;
    }
}