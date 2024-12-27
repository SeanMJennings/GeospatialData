using OSGeo.OGR;

Ogr.RegisterAll();
var geopackageFilePath = Environment.CurrentDirectory + @"\" + "example.gpkg";
using var ds1 = Ogr.Open( geopackageFilePath, 0 );
OutputLayers(ds1);

Console.WriteLine("=====================================");

var geoJsonFilePath = Environment.CurrentDirectory + @"\" + "schools-list.geojson";
using var ds2 = Ogr.Open( geoJsonFilePath, 0 );
OutputLayers(ds2);
return;

void OutputLayers(DataSource dataSource)
{
    for (var iLayer = 0; iLayer < dataSource.GetLayerCount(); iLayer++ )
    {
        var layer = dataSource.GetLayerByIndex(iLayer);
        Console.WriteLine(layer.GetName());
        for (var iFeature = 0; iFeature < layer.GetFeatureCount(0); iFeature++)
        {
            var feature = layer.GetFeature(iFeature);
            Console.WriteLine(feature?.GetFID());
            var geometry = feature?.GetGeometryRef();
            if (geometry != null)
            {
                Console.WriteLine(geometry.GetGeometryName());
            }
        }
    }
}