using NetTopologySuite.Features;
using NetTopologySuite.IO;
using Newtonsoft.Json;

var postgisContainer = await Common.Common.StartPostGisContainer();
var connection = await Common.Common.OpenDatabaseConnection(postgisContainer);
await Common.Common.CreateDataTable(connection);

var geoJsonFilePath = Environment.CurrentDirectory + @"\" + "schools-list.geojson";
var geoJson = File.ReadAllText(geoJsonFilePath);
var serializer = GeoJsonSerializer.Create();
using var stringReader = new StringReader(geoJson);
await using var jsonReader = new JsonTextReader(stringReader);
var features = serializer.Deserialize<FeatureCollection>(jsonReader);

await Common.Common.InsertFeaturesToTable(connection, features.ToArray());
await Common.Common.GetDataFromDatabase(connection);