using NetTopologySuite.IO.Esri;

var postgisContainer = await Common.Common.StartPostGisContainer();
var connection = await Common.Common.OpenDatabaseConnection(postgisContainer);
await Common.Common.CreateDataTable(connection);
var shapeFilePath = Environment.CurrentDirectory + "\\" + @"Wards_May_2024_Boundaries_UK_BFE_-3586170638928067737\WD_MAY_2024_UK_BFC.shp";
var features = Shapefile.ReadAllFeatures(shapeFilePath);


await Common.Common.InsertFeaturesToTable(connection, features);
await Common.Common.GetDataFromDatabase(connection);



