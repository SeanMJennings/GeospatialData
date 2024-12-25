using Npgsql;
using Testcontainers.PostgreSql;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Npgsql;
using Newtonsoft.Json;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace Common;

public static class Common
{
    public static async Task<PostgreSqlContainer> StartPostGisContainer()
    {
        var postgreSqlContainer = new PostgreSqlBuilder().WithImage("postgis/postgis:12-3.0").Build();
        await postgreSqlContainer.StartAsync();
        return postgreSqlContainer;
    }
    
    public static async Task<NpgsqlConnection> OpenDatabaseConnection(PostgreSqlContainer postgreSqlContainer)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(postgreSqlContainer.GetConnectionString()).EnableDynamicJson();
        dataSourceBuilder.UseNetTopologySuite();
        await using var dataSource = dataSourceBuilder.Build();
        var npgsqlConnection = await dataSource.OpenConnectionAsync();
        return npgsqlConnection;
    }
    
    public static async Task CreateDataTable(NpgsqlConnection npgsqlConnection)
    {
        await using var cmd = new NpgsqlCommand("CREATE TABLE data (geometry Geometry, boundary Geometry, attributes json);", npgsqlConnection);
        await cmd.ExecuteNonQueryAsync();
    }
    
    public static async Task InsertFeaturesToTable(NpgsqlConnection npgsqlConnection, IFeature[] features)
    {
        var geometryFactory = new GeometryFactory();
        await using var writer = await npgsqlConnection.BeginBinaryImportAsync("COPY data (geometry, boundary, attributes) FROM STDIN (FORMAT BINARY)");
        foreach (var feature in features)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(feature.Geometry, NpgsqlDbType.Geometry);
            if (feature.BoundingBox != null)
            {
                await writer.WriteAsync(geometryFactory.ToGeometry(feature.BoundingBox), NpgsqlDbType.Geometry);
            }
            else
            {
                await writer.WriteAsync<Geometry>(null, NpgsqlDbType.Geometry);  
            }
            await writer.WriteAsync(feature.Attributes, NpgsqlDbType.Json);
        }
        await writer.CompleteAsync();
    }
    
    public static async Task GetDataFromDatabase(NpgsqlConnection npgsqlConnection)
    {
        await using var cmd = new NpgsqlCommand("SELECT * FROM data", npgsqlConnection);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var value1 = reader.GetFieldValue<object>(0);
        var value2 = reader.GetFieldValue<object>(1);
        var value3 = JsonConvert.DeserializeObject(reader.GetFieldValue<string>(2));
    }
}