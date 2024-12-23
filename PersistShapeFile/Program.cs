using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;
using Newtonsoft.Json;

var postgisContainer = new PostgreSqlBuilder().WithImage("postgis/postgis:12-3.0").Build();
await postgisContainer.StartAsync();

var dataSourceBuilder = new NpgsqlDataSourceBuilder(postgisContainer.GetConnectionString()).EnableDynamicJson();
dataSourceBuilder.UseNetTopologySuite();
await using var dataSource = dataSourceBuilder.Build();
var connection = await dataSource.OpenConnectionAsync();
var shapeFilePath = Environment.CurrentDirectory + "\\" + @"Wards_May_2024_Boundaries_UK_BFE_-3586170638928067737\WD_MAY_2024_UK_BFC.shp";

// library does not support multipatch shapefiles. Multipatch seems to be quite uncommon.
var features = Shapefile.ReadAllFeatures(shapeFilePath);

await using (var cmd = new NpgsqlCommand("CREATE TABLE data (geometry Geometry, boundary Geometry, attributes json);", connection))
{
    await cmd.ExecuteNonQueryAsync();
}

var geometryFactory = new GeometryFactory();
await using (var writer = connection.BeginBinaryImport("COPY data (geometry, boundary, attributes) FROM STDIN (FORMAT BINARY)"))
{
    foreach (var feature in features)
    {
        writer.StartRow();
        writer.Write(feature.Geometry, NpgsqlDbType.Geometry);
        writer.Write(geometryFactory.ToGeometry(feature.BoundingBox), NpgsqlDbType.Geometry);
        writer.Write(feature.Attributes, NpgsqlDbType.Json);
    }
    writer.Complete();
}


await using (var cmd = new NpgsqlCommand("SELECT * FROM data", connection))
await using (var reader = await cmd.ExecuteReaderAsync())
{
    await reader.ReadAsync();
    var value1 = reader.GetFieldValue<object>(0);
    var value2 = reader.GetFieldValue<object>(1);
    var value3 = JsonConvert.DeserializeObject(reader.GetFieldValue<string>(2));
}