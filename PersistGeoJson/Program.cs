using NetTopologySuite.Features;
using NetTopologySuite.IO;
using Newtonsoft.Json;

var postgisContainer = await Common.Docker.StartPostGisContainer();
var connection = await Common.Persistence.OpenDatabaseConnection(postgisContainer);
await Common.Persistence.CreateDataTable(connection);

var geoJsonFilePath = Environment.CurrentDirectory + @"\" + "schools-list.geojson";
var geoJson = File.ReadAllText(geoJsonFilePath);
var serializer = GeoJsonSerializer.Create();
using var stringReader = new StringReader(geoJson);
await using var jsonReader = new JsonTextReader(stringReader);
var features = serializer.Deserialize<FeatureCollection>(jsonReader);

await Common.Persistence.InsertFeaturesToTable(connection, features.ToArray());
await Common.Persistence.GetDataFromDatabase(connection);