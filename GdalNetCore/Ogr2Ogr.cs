/******************************************************************************
 *
 * Project:  OpenGIS Simple Features Reference Implementation
 * Purpose:  Java port of a simple client for translating between formats.
 * Author:   Even Rouault, <even dot rouault at spatialys.com>
 *
 * Port from ogr2Ogr.cpp by Frank Warmerdam
 *
 ******************************************************************************
 * Copyright (c) 2009, Even Rouault
 * Copyright (c) 1999, Frank Warmerdam
 *
 * SPDX-License-Identifier: MIT
 ****************************************************************************/

using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;
using Driver = OSGeo.OGR.Driver;

/* Note : this is the most direct port of ogr2Ogr.cpp possible */
/* It could be made much more java'ish ! */

class GDALScaledProgress : ProgressCallback
{
    private double pctMin;
    private double pctMax;
    private ProgressCallback mainCbk;

    public GDALScaledProgress(double pctMin, double pctMax,
                              ProgressCallback mainCbk)
    {
        this.pctMin = pctMin;
        this.pctMax = pctMax;
        this.mainCbk = mainCbk;
    }

    public int run(double dfComplete, String message)
    {
        return mainCbk.run(pctMin + dfComplete * (pctMax - pctMin), message);
    }
};


public class ogr2ogr
{
    static bool bSkipFailures = false;
    static int nGroupTransactions = 200;
    static bool bPreserveFID = false;
    static int OGRNullFID = -1;
    static int nFIDToFetch = OGRNullFID;

    static class GeomOperation
    {
        private GeomOperation() {}
        public static GeomOperation NONE = new GeomOperation();
        public static GeomOperation SEGMENTIZE = new GeomOperation();
        public static GeomOperation SIMPLIFY_PRESERVE_TOPOLOGY = new GeomOperation();
    }

/************************************************************************/
/*                                main()                                */
/************************************************************************/

    public static void main(String[] args)
    {
        String pszFormat = "ESRI Shapefile";
        String pszDataSource = null;
        String pszDestDataSource = null;
        List<string> papszLayers = new List<string>();
        List<string> papszDSCO = new List<string>();
        List<string> papszLCO = new List<string>();
        bool bTransform = false;
        bool bAppend = false, bUpdate = false, bOverwrite = false;
        String pszOutputSRSDef = null;
        String pszSourceSRSDef = null;
        SpatialReference poOutputSRS = null;
        SpatialReference poSourceSRS = null;
        String pszNewLayerName = null;
        String pszWHERE = null;
        Geometry poSpatialFilter = null;
        String pszSelect;
        List<string> papszSelFields = null;
        String pszSQLStatement = null;
        int    eGType = -2;
        GeomOperation eGeomOp = GeomOperation.NONE;
        double dfGeomOpParam = 0;
        List<string> papszFieldTypesToString = new List<string>();
        bool bDisplayProgress = false;
        ProgressCallback pfnProgress = null;
        bool  bClipSrc = false;
        Geometry poClipSrc = null;
        String pszClipSrcDS = null;
        String pszClipSrcSQL = null;
        String pszClipSrcLayer = null;
        String pszClipSrcWhere = null;
        Geometry poClipDst = null;
        String pszClipDstDS = null;
        String pszClipDstSQL = null;
        String pszClipDstLayer = null;
        String pszClipDstWhere = null;
        String pszSrcEncoding = null;
        String pszDstEncoding = null;
        bool bExplodeCollections = false;
        String pszZField = null;

        Ogr.DontUseExceptions();

    /* -------------------------------------------------------------------- */
    /*      Register format(s).                                             */
    /* -------------------------------------------------------------------- */
        if(Ogr.GetDriverCount() == 0 )
            Ogr.RegisterAll();

    /* -------------------------------------------------------------------- */
    /*      Processing command line arguments.                              */
    /* -------------------------------------------------------------------- */
        args = Ogr.GeneralCmdLineProcessor(args, 0);

        if(args.Length < 2 )
        {
            Usage();
            Environment.Exit(-1);
        }

        for( int iArg = 0; iArg < args.Length; iArg++ )
        {
            if (args[iArg].Equals("-f", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszFormat = args[++iArg];
            }
            else if (args[iArg].Equals("-dsco", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                papszDSCO.addElement(args[++iArg] );
            }
            else if (args[iArg].Equals("-lco", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                papszLCO.addElement(args[++iArg] );
            }
            else if (args[iArg].Equals("-preserve_fid", StringComparison.OrdinalIgnoreCase))
            {
                bPreserveFID = true;
            }
            else if (args[iArg].Length >= 5 && args[iArg].Substring(0, 5).Equals("-skip", StringComparison.OrdinalIgnoreCase))
            {
                bSkipFailures = true;
                nGroupTransactions = 1; /* #2409 */
            }
            else if (args[iArg].Equals("-append", StringComparison.OrdinalIgnoreCase))
            {
                bAppend = true;
                bUpdate = true;
            }
            else if (args[iArg].Equals("-overwrite", StringComparison.OrdinalIgnoreCase))
            {
                bOverwrite = true;
                bUpdate = true;
            }
            else if (args[iArg].Equals("-update", StringComparison.OrdinalIgnoreCase))
            {
                bUpdate = true;
            }
            else if (args[iArg].Equals("-fid", StringComparison.OrdinalIgnoreCase) && iArg + 1 < args.Length)
            {
                nFIDToFetch = Integer.parseInt(args[++iArg]);
            }
            else if (args[iArg].Equals("-sql", StringComparison.OrdinalIgnoreCase) && iArg + 1 < args.Length)
            {
                pszSQLStatement = args[++iArg];
            }
            else if (args[iArg].Equals("-nln", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszNewLayerName = args[++iArg];
            }
            else if (args[iArg].Equals("-nlt", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                if (args[iArg + 1].Equals("NONE", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbNone;
                else if (args[iArg + 1].Equals("GEOMETRY", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbUnknown;
                else if (args[iArg + 1].Equals("POINT", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbPoint;
                else if (args[iArg + 1].Equals("LINESTRING", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbLineString;
                else if (args[iArg + 1].Equals("POLYGON", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbPolygon;
                else if (args[iArg + 1].Equals("GEOMETRYCOLLECTION", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbGeometryCollection;
                else if (args[iArg + 1].Equals("MULTIPOINT", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbMultiPoint;
                else if (args[iArg + 1].Equals("MULTILINESTRING", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbMultiLineString;
                else if (args[iArg + 1].Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbMultiPolygon;
                else if (args[iArg + 1].Equals("GEOMETRY25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbUnknown | (int)wkbGeometryType.wkb25DBit;
                else if (args[iArg + 1].Equals("POINT25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbPoint25D;
                else if (args[iArg + 1].Equals("LINESTRING25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbLineString25D;
                else if (args[iArg + 1].Equals("POLYGON25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbPolygon25D;
                else if (args[iArg + 1].Equals("GEOMETRYCOLLECTION25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbGeometryCollection25D;
                else if (args[iArg + 1].Equals("MULTIPOINT25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbMultiPoint25D;
                else if (args[iArg + 1].Equals("MULTILINESTRING25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbMultiLineString25D;
                else if (args[iArg + 1].Equals("MULTIPOLYGON25D", StringComparison.OrdinalIgnoreCase))
                    eGType = (int)wkbGeometryType.wkbMultiPolygon25D;
                else
                {
                    Console.Error.WriteLine("-nlt " + args[iArg + 1] + ": type not recognised.");
                    Environment.Exit(1);
                }
                iArg++;
            }
            else if ((args[iArg].Equals("-tg", StringComparison.OrdinalIgnoreCase) ||
                      args[iArg].Equals("-gt", StringComparison.OrdinalIgnoreCase)) && iArg < args.Length - 1)
            {
                nGroupTransactions = int.Parse(args[++iArg]);
            }
            else if (args[iArg].Equals("-s_srs", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszSourceSRSDef = args[++iArg];
            }
            else if (args[iArg].Equals("-a_srs", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszOutputSRSDef = args[++iArg];
            }
            else if (args[iArg].Equals("-t_srs", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszOutputSRSDef = args[++iArg];
                bTransform = true;
            }
            else if (args[iArg].Equals("-spat", StringComparison.OrdinalIgnoreCase) && iArg + 4 < args.Length)
            {
                Geometry oRing = new Geometry(wkbGeometryType.wkbLinearRing);
                double xmin = double.Parse(args[++iArg]);
                double ymin = double.Parse(args[++iArg]);
                double xmax = double.Parse(args[++iArg]);
                double ymax = double.Parse(args[++iArg]);
                oRing.AddPoint(xmin, ymin, 0);
                oRing.AddPoint(xmin, ymax, 0);
                oRing.AddPoint(xmax, ymax, 0);
                oRing.AddPoint(xmax, ymin, 0);
                oRing.AddPoint(xmin, ymin, 0);

                poSpatialFilter = new Geometry(wkbGeometryType.wkbPolygon);
                poSpatialFilter.AddGeometry(oRing);
            }
            else if (args[iArg].Equals("-where", StringComparison.OrdinalIgnoreCase) && iArg + 1 < args.Length)
            {
                pszWHERE = args[++iArg];
            }
            else if (args[iArg].Equals("-select", StringComparison.OrdinalIgnoreCase) && iArg + 1 < args.Length)
            {
                pszSelect = args[++iArg];
                var tokenizer = pszSelect.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                papszSelFields = tokenizer.ToList();
            }
            else if (args[iArg].Equals("-simplify", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                eGeomOp = GeomOperation.SIMPLIFY_PRESERVE_TOPOLOGY;
                dfGeomOpParam = double.Parse(args[++iArg]);
            }
            else if (args[iArg].Equals("-segmentize", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                eGeomOp = GeomOperation.SEGMENTIZE;
                dfGeomOpParam = double.Parse(args[++iArg]);
            }
            else if (args[iArg].Equals("-fieldTypeToString", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                var tokenizer = args[++iArg].Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokenizer)
                {
                    if (token.Equals("Integer", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("Real", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("String", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("Date", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("Time", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("DateTime", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("Binary", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("IntegerList", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("RealList", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("StringList", StringComparison.OrdinalIgnoreCase))
                    {
                        papszFieldTypesToString.Add(token);
                    }
                    else if (token.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        papszFieldTypesToString = null;
                        papszFieldTypesToString.Add("All");
                        break;
                    }
                    else
                    {
                        Console.Error.WriteLine("Unhandled type for fieldtypeasstring option : " + token);
                        Usage();
                    }
                }
            }
            else if (args[iArg].Equals("-progress", StringComparison.OrdinalIgnoreCase))
            {
                bDisplayProgress = true;
            }
            /*else if( args[iArg].equalsIgnoreCase("-wrapdateline") )
            {
                bWrapDateline = true;
            }
            */
            else if (args[iArg].Equals("-clipsrc", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                bClipSrc = true;
                if ( IsNumber(args[iArg+1]) && iArg < args.Length - 4 )
                {
                    Geometry oRing = new Geometry(wkbGeometryType.wkbLinearRing);
                    double xmin = double.Parse(args[++iArg]);
                    double ymin = double.Parse(args[++iArg]);
                    double xmax = double.Parse(args[++iArg]);
                    double ymax = double.Parse(args[++iArg]);
                    oRing.AddPoint(xmin, ymin, 0);
                    oRing.AddPoint(xmin, ymax, 0);
                    oRing.AddPoint(xmax, ymax, 0);
                    oRing.AddPoint(xmax, ymin, 0);
                    oRing.AddPoint(xmin, ymin, 0);

                    poClipSrc = new Geometry(wkbGeometryType.wkbPolygon);
                    poClipSrc.AddGeometry(oRing);
                }
                else if ((args[iArg + 1].Length >= 7 && args[iArg + 1].Substring(0, 7).Equals("POLYGON", StringComparison.OrdinalIgnoreCase)) ||
                         (args[iArg + 1].Length >= 12 && args[iArg + 1].Substring(0, 12).Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase)))
                {
                    poClipSrc = Geometry.CreateFromWkt(args[iArg+1]);
                    if (poClipSrc == null)
                    {
                        Console.Error.Write("FAILURE: Invalid geometry. Must be a valid POLYGON or MULTIPOLYGON WKT\n\n");
                        Usage();
                    }
                    iArg ++;
                }
                else if (args[iArg + 1].Equals("spat_extent", StringComparison.OrdinalIgnoreCase))
                {
                    iArg ++;
                }
                else
                {
                    pszClipSrcDS = args[iArg+1];
                    iArg ++;
                }
            }
            else if (args[iArg].Equals("-clipsrcsql", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipSrcSQL = args[iArg+1];
                iArg ++;
            }
            else if (args[iArg].Equals("-clipsrclayer", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipSrcLayer = args[iArg+1];
                iArg ++;
            }
            else if (args[iArg].Equals("-clipsrcwhere", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipSrcWhere = args[iArg+1];
                iArg ++;
            }
            else if (args[iArg].Equals("-clipdst", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                if ( IsNumber(args[iArg+1]) && iArg < args.Length - 4 )
                {
                    Geometry oRing = new Geometry(wkbGeometryType.wkbLinearRing);
                    double xmin = double.Parse(args[++iArg]);
                    double ymin = double.Parse(args[++iArg]);
                    double xmax = double.Parse(args[++iArg]);
                    double ymax = double.Parse(args[++iArg]);
                    oRing.AddPoint(xmin, ymin, 0);
                    oRing.AddPoint(xmin, ymax, 0);
                    oRing.AddPoint(xmax, ymax, 0);
                    oRing.AddPoint(xmax, ymin, 0);
                    oRing.AddPoint(xmin, ymin, 0);

                    poClipDst = new Geometry(wkbGeometryType.wkbPolygon);
                    poClipDst.AddGeometry(oRing);
                }
                else if ((args[iArg + 1].Length >= 7 && args[iArg + 1].Substring(0, 7).Equals("POLYGON", StringComparison.OrdinalIgnoreCase)) ||
                         (args[iArg + 1].Length >= 12 && args[iArg + 1].Substring(0, 12).Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase)))
                {
                    poClipDst = Geometry.CreateFromWkt(args[iArg+1]);
                    if (poClipDst == null)
                    {
                        Console.Error.Write("FAILURE: Invalid geometry. Must be a valid POLYGON or MULTIPOLYGON WKT\n\n");
                        Usage();
                    }
                    iArg ++;
                }
                else if (args[iArg + 1].Equals("spat_extent", StringComparison.OrdinalIgnoreCase))
                {
                    iArg ++;
                }
                else
                {
                    pszClipDstDS = args[iArg+1];
                    iArg ++;
                }
            }
            else if (args[iArg].Equals("-clipdstsql", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipDstSQL = args[iArg+1];
                iArg ++;
            }
            else if (args[iArg].Equals("-clipdstlayer", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipDstLayer = args[iArg + 1];
                iArg++;
            }
            else if (args[iArg].Equals("-clipdstwhere", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipDstWhere = args[iArg + 1];
                iArg++;
            }
            else if (args[iArg].Equals("-explodecollections", StringComparison.OrdinalIgnoreCase))
            {
                bExplodeCollections = true;
            }
            else if (args[iArg].Equals("-zfield", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszZField = args[iArg + 1];
                iArg++;
            }
            else if (args[iArg].StartsWith("-"))
            {
                Usage();
            }
            else if (pszDestDataSource == null)
                pszDestDataSource = args[iArg];
            else if (pszDataSource == null)
                pszDataSource = args[iArg];
            else
                papszLayers.Add(args[iArg]);
        }

        if( pszDataSource == null )
            Usage();

        if( bPreserveFID && bExplodeCollections )
        {
            Console.Error.Write("FAILURE: cannot load source clip geometry\n\n");
            Usage();
        }

        if( bClipSrc && pszClipSrcDS != null)
        {
            poClipSrc = LoadGeometry(pszClipSrcDS, pszClipSrcSQL, pszClipSrcLayer, pszClipSrcWhere);
            if (poClipSrc == null)
            {
                Console.Error.Write("FAILURE: cannot load source clip geometry\n\n");
                Usage();
            }
        }
        else if( bClipSrc && poClipSrc == null )
        {
            if (poSpatialFilter != null)
                poClipSrc = poSpatialFilter.Clone();
            if (poClipSrc == null)
            {
                Console.Error.Write("FAILURE: -clipsrc must be used with -spat option or a\n" +
                                    "bounding box,");
                Usage();
            }
        }

        if( pszClipDstDS != null)
        {
            poClipDst = LoadGeometry(pszClipDstDS, pszClipDstSQL, pszClipDstLayer, pszClipDstWhere);
            if (poClipDst == null)
            {
                Console.Error.Write("FAILURE: cannot load dest clip geometry\n\n" );
                Usage();
            }
        }
    /* -------------------------------------------------------------------- */
    /*      Open data source.                                               */
    /* -------------------------------------------------------------------- */
        DataSource poDS;

        poDS = Ogr.Open( pszDataSource, 0 );

    /* -------------------------------------------------------------------- */
    /*      Report failure                                                  */
    /* -------------------------------------------------------------------- */
        if( poDS == null )
        {
            Console.Error.Write("FAILURE:\n" +
                                "Unable to open datasource ` " + pszDataSource + "' with the following drivers.");

            for( int iDriver = 0; iDriver < Ogr.GetDriverCount(); iDriver++ )
            {
                Console.Error.Write("  . " + Ogr.GetDriver(iDriver).GetName() );
            }

            Environment.Exit(-1);
        }

    /* -------------------------------------------------------------------- */
    /*      Try opening the output datasource as an existing, writable      */
    /* -------------------------------------------------------------------- */
        DataSource       poODS = null;
        Driver poDriver = null;

        if( bUpdate )
        {
            poODS = Ogr.Open( pszDestDataSource, 1 );
            if( poODS == null )
            {
                if (bOverwrite || bAppend)
                {
                    poODS = Ogr.Open( pszDestDataSource, 0 );
                    if ( poODS == null )
                    {
                        /* ok the datasource doesn't exist at all */
                        bUpdate = false;
                    }
                    else
                    {
                        poODS.Dispose();
                        poODS = null;
                    }
                }

                if (bUpdate)
                {
                    Console.Error.Write("FAILURE:\n" +
                            "Unable to open existing output datasource `" + pszDestDataSource + "'.");
                   Environment.Exit(-1);
                }
            }

            else if( papszDSCO.size() > 0 )
            {
                Console.Error.Write("WARNING: Datasource creation options ignored since an existing datasource\n" +
                        "         being updated." );
            }

            if (poODS != null)
                poDriver = poODS.GetDriver();
        }

    /* -------------------------------------------------------------------- */
    /*      Find the output driver.                                         */
    /* -------------------------------------------------------------------- */
        if( !bUpdate )
        {
            int                  iDriver;

            poDriver = Ogr.GetDriverByName(pszFormat);
            if( poDriver == null )
            {
                Console.Error.Write("Unable to find driver `" + pszFormat +"'." );
                Console.Error.Write( "The following drivers are available:" );

                for( iDriver = 0; iDriver < Ogr.GetDriverCount(); iDriver++ )
                {
                    Console.Error.Write("  . " + Ogr.GetDriver(iDriver).GetName() );
                }
               Environment.Exit(-1);
            }

            if( poDriver.TestCapability( Ogr.ODrCCreateDataSource ) == false )
            {
                Console.Error.Write( pszFormat + " driver does not support data source creation.");
               Environment.Exit(-1);
            }

    /* -------------------------------------------------------------------- */
    /*      Special case to improve user experience when translating        */
    /*      a datasource with multiple layers into a shapefile. If the      */
    /*      user gives a target datasource with .shp and it does not exist, */
    /*      the shapefile driver will try to create a file, but this is not */
    /*      appropriate because here we have several layers, so create      */
    /*      a directory instead.                                            */
    /* -------------------------------------------------------------------- */
            if (poDriver.GetName().equalsIgnoreCase("ESRI Shapefile") &&
                pszSQLStatement == null &&
                (papszLayers.size() > 1 ||
                 (papszLayers.size() == 0 && poDS.GetLayerCount() > 1)) &&
                pszNewLayerName == null &&
                (pszDestDataSource.endsWith(".shp") || pszDestDataSource.endsWith(".SHP")))
            {
                File f = new File(pszDestDataSource);
                if (!f.exists())
                {
                    if (!f.mkdir())
                    {
                        Console.Error.Write(
                            "Failed to create directory " + pszDestDataSource + "\n" +
                            "for shapefile datastore.");
                        Environment.Exit(-1);
                    }
                }
            }

    /* -------------------------------------------------------------------- */
    /*      Create the output data source.                                  */
    /* -------------------------------------------------------------------- */
            poODS = poDriver.CreateDataSource( pszDestDataSource, papszDSCO );
            if( poODS == null )
            {
                Console.Error.Write( pszFormat + " driver failed to create "+ pszDestDataSource );
               Environment.Exit(-1);
            }
        }

    /* -------------------------------------------------------------------- */
    /*      Parse the output SRS definition if possible.                    */
    /* -------------------------------------------------------------------- */
        if( pszOutputSRSDef != null )
        {
            poOutputSRS = new SpatialReference();
            if( poOutputSRS.SetFromUserInput( pszOutputSRSDef ) != 0 )
            {
                Console.Error.Write( "Failed to process SRS definition: " + pszOutputSRSDef );
               Environment.Exit(-1);
            }
        }

    /* -------------------------------------------------------------------- */
    /*      Parse the source SRS definition if possible.                    */
    /* -------------------------------------------------------------------- */
        if( pszSourceSRSDef != null )
        {
            poSourceSRS = new SpatialReference();
            if( poSourceSRS.SetFromUserInput( pszSourceSRSDef ) != 0 )
            {
                Console.Error.Write( "Failed to process SRS definition: " + pszSourceSRSDef );
               Environment.Exit(-1);
            }
        }

    /* -------------------------------------------------------------------- */
    /*      Special case for -sql clause.  No source layers required.       */
    /* -------------------------------------------------------------------- */
        if( pszSQLStatement != null )
        {
            Layer poResultSet;

            if( pszWHERE != null )
                Console.Error.Write( "-where clause ignored in combination with -sql." );
            if( papszLayers.size() > 0 )
                Console.Error.Write( "layer names ignored in combination with -sql." );

            poResultSet = poDS.ExecuteSQL( pszSQLStatement, poSpatialFilter,
                                            null );

            if( poResultSet != null )
            {
                long nCountLayerFeatures = 0;
                if (bDisplayProgress)
                {
                    if (!poResultSet.TestCapability(Ogr.OLCFastFeatureCount))
                    {
                        Console.Error.Write( "Progress turned off as fast feature count is not available.");
                        bDisplayProgress = false;
                    }
                    else
                    {
                        nCountLayerFeatures = poResultSet.GetFeatureCount();
                        pfnProgress = new TermProgressCallback();
                    }
                }

/* -------------------------------------------------------------------- */
/*      Special case to improve user experience when translating into   */
/*      single file shapefile and source has only one layer, and that   */
/*      the layer name isn't specified                                  */
/* -------------------------------------------------------------------- */
                if (poDriver.GetName().equalsIgnoreCase("ESRI Shapefile") &&
                    pszNewLayerName == null)
                {
                    File f = new File(pszDestDataSource);
                    if (f.exists() && f.listFiles() == null)
                    {
                        pszNewLayerName = f.getName();
                        int posPoint = pszNewLayerName.lastIndexOf('.');
                        if (posPoint != -1)
                            pszNewLayerName = pszNewLayerName.substring(0, posPoint);
                    }
                }

                if( !TranslateLayer( poDS, poResultSet, poODS, papszLCO,
                                    pszNewLayerName, bTransform, poOutputSRS,
                                    poSourceSRS, papszSelFields, bAppend, eGType,
                                    bOverwrite, eGeomOp, dfGeomOpParam, papszFieldTypesToString,
                                    nCountLayerFeatures, poClipSrc, poClipDst, bExplodeCollections,
                                    pszZField, pszWHERE, pfnProgress ))
                {
                    Console.Error.Write(
                            "Terminating translation prematurely after failed\n" +
                            "translation from sql statement." );

                   Environment.Exit(-1);
                }
                poDS.ReleaseResultSet( poResultSet );
            }
        }
        else
        {
            int nLayerCount = 0;
            Layer[] papoLayers = null;

    /* -------------------------------------------------------------------- */
    /*      Process each data source layer.                                 */
    /* -------------------------------------------------------------------- */
            if ( papszLayers.size() == 0)
            {
                nLayerCount = poDS.GetLayerCount();
                papoLayers = new Layer[nLayerCount];

                for( int iLayer = 0;
                    iLayer < nLayerCount;
                    iLayer++ )
                {
                    Layer        poLayer = poDS.GetLayer(iLayer);

                    if( poLayer == null )
                    {
                        Console.Error.Write("FAILURE: Couldn't fetch advertised layer " + iLayer + "!");
                       Environment.Exit(-1);
                    }

                    papoLayers[iLayer] = poLayer;
                }
            }
    /* -------------------------------------------------------------------- */
    /*      Process specified data source layers.                           */
    /* -------------------------------------------------------------------- */
            else
            {
                nLayerCount = papszLayers.size();
                papoLayers = new Layer[nLayerCount];

                for( int iLayer = 0;
                    iLayer < papszLayers.size();
                    iLayer++ )
                {
                    Layer        poLayer = poDS.GetLayerByName((String)papszLayers.get(iLayer));

                    if( poLayer == null )
                    {
                        Console.Error.Write("FAILURE: Couldn't fetch advertised layer " + (String)papszLayers.get(iLayer) + "!");
                       Environment.Exit(-1);
                    }

                    papoLayers[iLayer] = poLayer;
                }
            }

/* -------------------------------------------------------------------- */
/*      Special case to improve user experience when translating into   */
/*      single file shapefile and source has only one layer, and that   */
/*      the layer name isn't specified                                  */
/* -------------------------------------------------------------------- */
            if (poDriver.GetName().equalsIgnoreCase("ESRI Shapefile") &&
                nLayerCount == 1 && pszNewLayerName == null)
            {
                File f = new File(pszDestDataSource);
                if (f.exists() && f.listFiles() == null)
                {
                    pszNewLayerName = f.getName();
                    int posPoint = pszNewLayerName.lastIndexOf('.');
                    if (posPoint != -1)
                        pszNewLayerName = pszNewLayerName.substring(0, posPoint);
                }
            }

            long[] panLayerCountFeatures = new long[nLayerCount];
            long nCountLayersFeatures = 0;
            long nAccCountFeatures = 0;

            /* First pass to apply filters and count all features if necessary */
            for( int iLayer = 0;
                iLayer < nLayerCount;
                iLayer++ )
            {
                Layer        poLayer = papoLayers[iLayer];

                if( pszWHERE != null )
                {
                    if( poLayer.SetAttributeFilter( pszWHERE ) != Ogr.OGRERR_NONE )
                    {
                        Console.Error.Write("FAILURE: SetAttributeFilter(" + pszWHERE + ") failed.");
                        if (!bSkipFailures)
                           Environment.Exit(-1);
                    }
                }

                if( poSpatialFilter != null )
                    poLayer.SetSpatialFilter( poSpatialFilter );

                if (bDisplayProgress)
                {
                    if (!poLayer.TestCapability(Ogr.OLCFastFeatureCount))
                    {
                        Console.Error.Write("Progress turned off as fast feature count is not available.");
                        bDisplayProgress = false;
                    }
                    else
                    {
                        panLayerCountFeatures[iLayer] = poLayer.GetFeatureCount();
                        nCountLayersFeatures += panLayerCountFeatures[iLayer];
                    }
                }
            }

            /* Second pass to do the real job */
            for( int iLayer = 0;
                iLayer < nLayerCount;
                iLayer++ )
            {
                Layer        poLayer = papoLayers[iLayer];

                if (bDisplayProgress)
                {
                    pfnProgress = new GDALScaledProgress(
                            nAccCountFeatures * 1.0 / nCountLayersFeatures,
                            (nAccCountFeatures + panLayerCountFeatures[iLayer]) * 1.0 / nCountLayersFeatures,
                            new TermProgressCallback());
                }

                nAccCountFeatures += panLayerCountFeatures[iLayer];

                if( !TranslateLayer( poDS, poLayer, poODS, papszLCO,
                                    pszNewLayerName, bTransform, poOutputSRS,
                                    poSourceSRS, papszSelFields, bAppend, eGType,
                                    bOverwrite, eGeomOp, dfGeomOpParam, papszFieldTypesToString,
                                    panLayerCountFeatures[iLayer], poClipSrc, poClipDst, bExplodeCollections,
                                    pszZField, pszWHERE, pfnProgress)
                    && !bSkipFailures )
                {
                    Console.Error.Write(
                            "Terminating translation prematurely after failed\n" +
                            "translation of layer " + poLayer.GetLayerDefn().GetName() + " (use -skipfailures to skip errors)");

                   Environment.Exit(-1);
                }
            }
        }

    /* -------------------------------------------------------------------- */
    /*      Close down.                                                     */
    /* -------------------------------------------------------------------- */
        /* We must explicitly destroy the output dataset in order the file */
        /* to be properly closed ! */
        poODS.delete();
        poDS.delete();
    }

    /************************************************************************/
    /*                               Usage()                                */
    /************************************************************************/

    static void Usage()

    {
        System.out.print( "Usage: ogr2ogr [--help-general] [-skipfailures] [-append] [-update] [-gt n]\n" +
                "               [-select field_list] [-where restricted_where] \n" +
                "               [-progress] [-sql <sql statement>] \n" +
                "               [-spat xmin ymin xmax ymax] [-preserve_fid] [-fid FID]\n" +
                "               [-a_srs srs_def] [-t_srs srs_def] [-s_srs srs_def]\n" +
                "               [-f format_name] [-overwrite] [[-dsco NAME=VALUE] ...]\n" +
                "               [-simplify tolerance]\n" +
                // "               [-segmentize max_dist] [-fieldTypeToString All|(type1[,type2]*)]\n" +
                "               [-fieldTypeToString All|(type1[,type2]*)] [-explodecollections]\n" +
                "               dst_datasource_name src_datasource_name\n" +
                "               [-lco NAME=VALUE] [-nln name] [-nlt type] [layer [layer ...]]\n" +
                "\n" +
                " -f format_name: output file format name, possible values are:\n");

        for( int iDriver = 0; iDriver < Ogr.GetDriverCount(); iDriver++ )
        {
            Driver poDriver = Ogr.GetDriver(iDriver);

            if( poDriver.TestCapability( Ogr.ODrCCreateDataSource ) )
                System.out.print( "     -f \"" + poDriver.GetName() + "\"\n" );
        }

        System.out.print( " -append: Append to existing layer instead of creating new if it exists\n" +
                " -overwrite: delete the output layer and recreate it empty\n" +
                " -update: Open existing output datasource in update mode\n" +
                " -progress: Display progress on terminal. Only works if input layers have the \"fast feature count\" capability\n" +
                " -select field_list: Comma-delimited list of fields from input layer to\n" +
                "                     copy to the new layer (defaults to all)\n" +
                " -where restricted_where: Attribute query (like SQL WHERE)\n" +
                " -sql statement: Execute given SQL statement and save result.\n" +
                " -skipfailures: skip features or layers that fail to convert\n" +
                " -gt n: group n features per transaction (default 200)\n" +
                " -spat xmin ymin xmax ymax: spatial query extents\n" +
                " -simplify tolerance: distance tolerance for simplification.\n" +
                //" -segmentize max_dist: maximum distance between 2 nodes.\n" +
                //"                       Used to create intermediate points\n" +
                " -dsco NAME=VALUE: Dataset creation option (format specific)\n" +
                " -lco  NAME=VALUE: Layer creation option (format specific)\n" +
                " -nln name: Assign an alternate name to the new layer\n" +
                " -nlt type: Force a geometry type for new layer.  One of NONE, GEOMETRY,\n" +
                "      POINT, LINESTRING, POLYGON, GEOMETRYCOLLECTION, MULTIPOINT,\n" +
                "      MULTIPOLYGON, or MULTILINESTRING.  Add \"25D\" for 3D layers.\n" +
                "      Default is type of source layer.\n" +
                " -fieldTypeToString type1,...: Converts fields of specified types to\n" +
                "      fields of type string in the new layer. Valid types are : \n" +
                "      Integer, Real, String, Date, Time, DateTime, Binary, IntegerList, RealList,\n" +
                "      StringList. Special value All can be used to convert all fields to strings.\n");

        System.out.print(" -a_srs srs_def: Assign an output SRS\n" +
            " -t_srs srs_def: Reproject/transform to this SRS on output\n" +
            " -s_srs srs_def: Override source SRS\n" +
            "\n" +
            " Srs_def can be a full WKT definition (hard to escape properly),\n" +
            " or a well known definition (i.e. EPSG:4326) or a file with a WKT\n" +
            " definition.\n" );

       Environment.Exit(-1);
    }

    static int CSLFindString(List<string> v, String str)
    {
        int i = 0;
        Enumeration e = v.elements();
        while(e.hasMoreElements())
        {
            String strIter = (String)e.nextElement();
            if (strIter.equalsIgnoreCase(str))
                return i;
            i ++;
        }
        return -1;
    }

    static bool IsNumber(String pszStr)
    {
        try
        {
            Double.parseDouble(pszStr);
            return true;
        }
        catch(Exception ex)
        {
            return false;
        }
    }

    static Geometry LoadGeometry( String pszDS,
                                  String pszSQL,
                                  String pszLyr,
                                  String pszWhere)
    {
        DataSource       poDS;
        Layer            poLyr;
        Feature          poFeat;
        Geometry         poGeom = null;

        poDS = Ogr.Open( pszDS, false );
        if (poDS == null)
            return null;

        if (pszSQL != null)
            poLyr = poDS.ExecuteSQL( pszSQL, null, null );
        else if (pszLyr != null)
            poLyr = poDS.GetLayerByName(pszLyr);
        else
            poLyr = poDS.GetLayer(0);

        if (poLyr == null)
        {
            System.err.print("Failed to identify source layer from datasource.\n");
            poDS.delete();
            return null;
        }

        if (pszWhere != null)
            poLyr.SetAttributeFilter(pszWhere);

        while ((poFeat = poLyr.GetNextFeature()) != null)
        {
            Geometry poSrcGeom = poFeat.GetGeometryRef();
            if (poSrcGeom != null)
            {
                int eType = wkbFlatten(poSrcGeom.GetGeometryType());

                if (poGeom == null)
                    poGeom = new Geometry( wkbGeometryType.wkbMultiPolygon );

                if( eType == (int)wkbGeometryType.wkbPolygon )
                    poGeom.AddGeometry( poSrcGeom );
                else if( eType == (int)wkbGeometryType.wkbMultiPolygon )
                {
                    int iGeom;
                    int nGeomCount = poSrcGeom.GetGeometryCount();

                    for( iGeom = 0; iGeom < nGeomCount; iGeom++ )
                    {
                        poGeom.AddGeometry(poSrcGeom.GetGeometryRef(iGeom) );
                    }
                }
                else
                {
                    Console.Error.Write("ERROR: Geometry not of polygon type.\n" );
                    if( pszSQL != null )
                        poDS.ReleaseResultSet( poLyr );
                    poDS.Dispose();
                    return null;
                }
            }
        }

        if( pszSQL != null )
            poDS.ReleaseResultSet( poLyr );
        poDS.Dispose();

        return poGeom;
    }


    static int wkbFlatten(int eType)
    {
        return eType & (~ogrConstants.wkb25DBit);
    }

    /************************************************************************/
    /*                               SetZ()                                 */
    /************************************************************************/
    static void SetZ (Geometry poGeom, double dfZ )
    {
        if (poGeom == null)
            return;
        switch (wkbFlatten((int)poGeom.GetGeometryType()))
        {
            case (int)wkbGeometryType.wkbPoint:
                poGeom.SetPoint(0, (int)poGeom.GetX(0), (int)poGeom.GetY(0), dfZ);
                break;

            case (int)wkbGeometryType.wkbLineString:
            case (int)wkbGeometryType.wkbLinearRing:
            {
                int i;
                for(i=0;i<poGeom.GetPointCount();i++)
                    poGeom.SetPoint(i, poGeom.GetX(i), poGeom.GetY(i), dfZ);
                break;
            }

            case (int)wkbGeometryType.wkbPolygon:
            case (int)wkbGeometryType.wkbMultiPoint:
            case (int)wkbGeometryType.wkbMultiLineString:
            case (int)wkbGeometryType.wkbMultiPolygon:
            case (int)wkbGeometryType.wkbGeometryCollection:
            {
                int i;
                for(i=0;i<poGeom.GetGeometryCount();i++)
                    SetZ(poGeom.GetGeometryRef(i), dfZ);
                break;
            }

            default:
                break;
        }
    }


    /************************************************************************/
    /*                           TranslateLayer()                           */
    /************************************************************************/

    static bool TranslateLayer( DataSource poSrcDS,
                            Layer poSrcLayer,
                            DataSource poDstDS,
                            List<string> papszLCO,
                            String pszNewLayerName,
                            bool bTransform,
                            SpatialReference poOutputSRS,
                            SpatialReference poSourceSRS,
                            List<string> papszSelFields,
                            bool bAppend, int eGType, bool bOverwrite,
                            GeomOperation eGeomOp,
                            double dfGeomOpParam,
                            List<string> papszFieldTypesToString,
                            long nCountLayerFeatures,
                            Geometry poClipSrc,
                            Geometry poClipDst,
                            bool bExplodeCollections,
                            String pszZField,
                            String pszWHERE,
                            ProgressCallback pfnProgress)

    {
        Layer    poDstLayer;
        FeatureDefn poSrcFDefn;
        int      eErr;
        bool         bForceToPolygon = false;
        bool         bForceToMultiPolygon = false;
        bool         bForceToMultiLineString = false;

        if( pszNewLayerName == null )
            pszNewLayerName = poSrcLayer.GetLayerDefn().GetName();

        if( wkbFlatten(eGType) == (int)wkbGeometryType.wkbPolygon )
            bForceToPolygon = true;
        else if( wkbFlatten(eGType) == (int)wkbGeometryType.wkbMultiPolygon )
            bForceToMultiPolygon = true;
        else if( wkbFlatten(eGType) == (int)wkbGeometryType.wkbMultiLineString )
            bForceToMultiLineString = true;

    /* -------------------------------------------------------------------- */
    /*      Setup coordinate transformation if we need it.                  */
    /* -------------------------------------------------------------------- */
        CoordinateTransformation poCT = null;

        if( bTransform )
        {
            if( poSourceSRS == null )
                poSourceSRS = poSrcLayer.GetSpatialRef();

            if( poSourceSRS == null )
            {
                Console.Error.Write("Can't transform coordinates, source layer has no\n" +
                        "coordinate system.  Use -s_srs to set one." );
               Environment.Exit(-1);
            }

            /*CPLAssert( null != poSourceSRS );
            CPLAssert( null != poOutputSRS );*/

            /* New in GDAL 1.10. Before was "new CoordinateTransformation(srs,dst)". */
            poCT = CoordinateTransformation.CreateCoordinateTransformation( poSourceSRS, poOutputSRS );
            if( poCT == null )
            {
                String pszWKT = null;

                Console.Error.Write("Failed to create coordinate transformation between the\n" +
                    "following coordinate systems.  This may be because they\n" +
                    "are not transformable, or because projection services\n" +
                    "(PROJ.4 DLL/.so) could not be loaded." );

                pszWKT = poSourceSRS.ExportToPrettyWkt( 0 );
                Console.Error.Write( "Source:\n" + pszWKT );

                pszWKT = poOutputSRS.ExportToPrettyWkt( 0 );
                Console.Error.Write( "Target:\n" + pszWKT );
               Environment.Exit(-1);
            }
        }

    /* -------------------------------------------------------------------- */
    /*      Get other info.                                                 */
    /* -------------------------------------------------------------------- */
        poSrcFDefn = poSrcLayer.GetLayerDefn();

        if( poOutputSRS == null )
            poOutputSRS = poSrcLayer.GetSpatialRef();

    /* -------------------------------------------------------------------- */
    /*      Find the layer.                                                 */
    /* -------------------------------------------------------------------- */

        /* GetLayerByName() can instantiate layers that would have been */
        /* 'hidden' otherwise, for example, non-spatial tables in a */
        /* PostGIS-enabled database, so this apparently useless command is */
        /* not useless. (#4012) */
        Gdal.PushErrorHandler("CPLQuietErrorHandler");
        poDstLayer = poDstDS.GetLayerByName(pszNewLayerName);
        Gdal.PopErrorHandler();
        Gdal.ErrorReset();

        int iLayer = -1;
        if( poDstLayer != null )
        {
            int nLayerCount = poDstDS.GetLayerCount();
            for( iLayer = 0; iLayer < nLayerCount; iLayer++ )
            {
                Layer        poLayer = poDstDS.GetLayerByIndex(iLayer);

                if( poLayer != null
                    && poLayer.GetName().Equals(poDstLayer.GetName()) )
                {
                    break;
                }
            }

            if (iLayer == nLayerCount)
                /* shouldn't happen with an ideal driver */
                poDstLayer = null;
        }

    /* -------------------------------------------------------------------- */
    /*      If the user requested overwrite, and we have the layer in       */
    /*      question we need to delete it now so it will get recreated      */
    /*      (overwritten).                                                  */
    /* -------------------------------------------------------------------- */
        if( poDstLayer != null && bOverwrite )
        {
            if( poDstDS.DeleteLayer( iLayer ) != 0 )
            {
                Console.Error.Write(
                        "DeleteLayer() failed when overwrite requested." );
                return false;
            }
            poDstLayer = null;
        }

    /* -------------------------------------------------------------------- */
    /*      If the layer does not exist, then create it.                    */
    /* -------------------------------------------------------------------- */
        if( poDstLayer == null )
        {
            if( eGType == -2 )
            {
                eGType = (int)poSrcFDefn.GetGeomType();

                if ( bExplodeCollections )
                {
                    int n25DBit = eGType & Ogr.wkb25DBit;
                    if (wkbFlatten(eGType) == (int)wkbGeometryType.wkbMultiPoint)
                    {
                        eGType = (int)wkbGeometryType.wkbPoint | n25DBit;
                    }
                    else if (wkbFlatten(eGType) == (int)wkbGeometryType.wkbMultiLineString)
                    {
                        eGType = (int)wkbGeometryType.wkbLineString | n25DBit;
                    }
                    else if (wkbFlatten(eGType) == (int)wkbGeometryType.wkbMultiPolygon)
                    {
                        eGType = (int)wkbGeometryType.wkbPolygon | n25DBit;
                    }
                    else if (wkbFlatten(eGType) == (int)wkbGeometryType.wkbGeometryCollection)
                    {
                        eGType = (int)wkbGeometryType.wkbUnknown | n25DBit;
                    }
                }

                if ( pszZField != null )
                    eGType |= Ogr.wkb25DBit;
            }

            if( poDstDS.TestCapability( Ogr.ODsCCreateLayer ) == false)
            {
                Console.Error.Write(
                "Layer " + pszNewLayerName + "not found, and CreateLayer not supported by driver.");
                return false;
            }

            Gdal.ErrorReset();

            poDstLayer = poDstDS.CreateLayer( pszNewLayerName, poOutputSRS,
                                              eGType, papszLCO );

            if( poDstLayer == null )
                return false;

            bAppend = false;
        }

    /* -------------------------------------------------------------------- */
    /*      Otherwise we will append to it, if append was requested.        */
    /* -------------------------------------------------------------------- */
        else if( !bAppend )
        {
            Console.Error.Write("FAILED: Layer " + pszNewLayerName + "already exists, and -append not specified.\n" +
                                "        Consider using -append, or -overwrite.");
            return false;
        }
        else
        {
            if( papszLCO.Count > 0 )
            {
                Console.Error.Write("WARNING: Layer creation options ignored since an existing layer is\n" +
                        "         being appended to." );
            }
        }

    /* -------------------------------------------------------------------- */
    /*      Add fields.  Default to copy all field.                         */
    /*      If only a subset of all fields requested, then output only      */
    /*      the selected fields, and in the order that they were            */
    /*      selected.                                                       */
    /* -------------------------------------------------------------------- */
        int         iField;

        /* Initialize the index-to-index map to -1's */
        int nSrcFieldCount = poSrcFDefn.GetFieldCount();
        int[] panMap = new int [nSrcFieldCount];
        for( iField=0; iField < nSrcFieldCount; iField++)
            panMap[iField] = -1;

        FeatureDefn poDstFDefn = poDstLayer.GetLayerDefn();

        if (papszSelFields != null && !bAppend )
        {
            int  nDstFieldCount = 0;
            if (poDstFDefn != null)
                nDstFieldCount = poDstFDefn.GetFieldCount();

            for( iField=0; iField < papszSelFields.Count; iField++)
            {
                int iSrcField = poSrcFDefn.GetFieldIndex((String)papszSelFields.get(iField));
                if (iSrcField >= 0)
                {
                    FieldDefn poSrcFieldDefn = poSrcFDefn.GetFieldDefn(iSrcField);
                    FieldDefn oFieldDefn = new FieldDefn( poSrcFieldDefn.GetNameRef(),
                                                poSrcFieldDefn.GetFieldType() );
                    oFieldDefn.SetWidth( poSrcFieldDefn.GetWidth() );
                    oFieldDefn.SetPrecision( poSrcFieldDefn.GetPrecision() );

                    if (papszFieldTypesToString != null &&
                        (CSLFindString(papszFieldTypesToString, "All") != -1 ||
                        CSLFindString(papszFieldTypesToString,
                                    Ogr.GetFieldTypeName(poSrcFDefn.GetFieldDefn(iSrcField).GetFieldType())) != -1))
                        oFieldDefn.SetType(Ogr.OFTString);

                    /* The field may have been already created at layer creation */
                    int iDstField = -1;
                    if (poDstFDefn != null)
                        iDstField = poDstFDefn.GetFieldIndex(oFieldDefn.GetNameRef());
                    if (iDstField >= 0)
                    {
                        panMap[iSrcField] = iDstField;
                    }
                    else if (poDstLayer.CreateField( oFieldDefn, 0 ) == 0)
                    {
                        /* now that we've created a field, GetLayerDefn() won't return NULL */
                        if (poDstFDefn == null)
                            poDstFDefn = poDstLayer.GetLayerDefn();

                        /* Sanity check : if it fails, the driver is buggy */
                        if (poDstFDefn != null &&
                            poDstFDefn.GetFieldCount() != nDstFieldCount + 1)
                        {
                            Console.Error.Write(
                                    "The output driver has claimed to have added the " + oFieldDefn.GetNameRef() + " field, but it did not!");
                        }
                        else
                        {
                            panMap[iSrcField] = nDstFieldCount;
                            nDstFieldCount ++;
                        }
                    }

                }
                else
                {
                    Console.Error.Write("Field '" + (String)papszSelFields.get(iField) + "' not found in source layer.");
                        if( !bSkipFailures )
                            return false;
                }
            }

            /* -------------------------------------------------------------------- */
            /* Use SetIgnoredFields() on source layer if available                  */
            /* -------------------------------------------------------------------- */

            /* Here we differ from the ogr2Ogr.cpp implementation since the OGRFeatureQuery */
            /* isn't mapped to swig. So in that case just don't use SetIgnoredFields() */
            /* to avoid issue raised in #4015 */
            if (poSrcLayer.TestCapability(Ogr.OLCIgnoreFields) && pszWHERE == null)
            {
                int iSrcField;
                List<string> papszIgnoredFields = new List<string>();
                for(iSrcField=0;iSrcField<nSrcFieldCount;iSrcField++)
                {
                    String pszFieldName =
                        poSrcFDefn.GetFieldDefn(iSrcField).GetNameRef();
                    bool bFieldRequested = false;
                    for( iField=0; iField < papszSelFields.Count; iField++)
                    {
                        if (pszFieldName.equalsIgnoreCase((String)papszSelFields.get(iField)))
                        {
                            bFieldRequested = true;
                            break;
                        }
                    }

                    if (pszZField != null && pszFieldName.equalsIgnoreCase(pszZField))
                        bFieldRequested = true;

                    /* If source field not requested, add it to ignored files list */
                    if (!bFieldRequested)
                        papszIgnoredFields.Add(pszFieldName);
                }
                poSrcLayer.SetIgnoredFields(papszIgnoredFields);
            }
        }
        else if( !bAppend )
        {
            int nDstFieldCount = 0;
            if (poDstFDefn != null)
                nDstFieldCount = poDstFDefn.GetFieldCount();
            for( iField = 0; iField < nSrcFieldCount; iField++ )
            {
                FieldDefn poSrcFieldDefn = poSrcFDefn.GetFieldDefn(iField);
                FieldDefn oFieldDefn = new FieldDefn( poSrcFieldDefn.GetNameRef(),
                                            poSrcFieldDefn.GetFieldType() );
                oFieldDefn.SetWidth( poSrcFieldDefn.GetWidth() );
                oFieldDefn.SetPrecision( poSrcFieldDefn.GetPrecision() );

                if (papszFieldTypesToString != null &&
                    (CSLFindString(papszFieldTypesToString, "All") != -1 ||
                    CSLFindString(papszFieldTypesToString,
                                Ogr.GetFieldTypeName(poSrcFDefn.GetFieldDefn(iField).GetFieldType())) != -1))
                    oFieldDefn.SetType(Ogr.OFTString);

                /* The field may have been already created at layer creation */
                int iDstField = -1;
                if (poDstFDefn != null)
                    iDstField = poDstFDefn.GetFieldIndex(oFieldDefn.GetNameRef());
                if (iDstField >= 0)
                {
                    panMap[iField] = iDstField;
                }
                else if (poDstLayer.CreateField( oFieldDefn, 0 ) == 0)
                {
                    /* now that we've created a field, GetLayerDefn() won't return NULL */
                    if (poDstFDefn == null)
                        poDstFDefn = poDstLayer.GetLayerDefn();

                    /* Sanity check : if it fails, the driver is buggy */
                    if (poDstFDefn != null &&
                        poDstFDefn.GetFieldCount() != nDstFieldCount + 1)
                    {
                        Console.Error.Write(
                                "The output driver has claimed to have added the " + oFieldDefn.GetNameRef() + " field, but it did not!");
                    }
                    else
                    {
                        panMap[iField] = nDstFieldCount;
                        nDstFieldCount ++;
                    }
                }
            }
        }
        else
        {
            /* For an existing layer, build the map by fetching the index in the destination */
            /* layer for each source field */
            if (poDstFDefn == null)
            {
                Console.Error.Write("poDstFDefn == NULL.\n" );
                return false;
            }

            for( iField = 0; iField < nSrcFieldCount; iField++ )
            {
                FieldDefn poSrcFieldDefn = poSrcFDefn.GetFieldDefn(iField);
                int iDstField = poDstFDefn.GetFieldIndex(poSrcFieldDefn.GetNameRef());
                if (iDstField >= 0)
                    panMap[iField] = iDstField;
            }
        }

    /* -------------------------------------------------------------------- */
    /*      Transfer features.                                              */
    /* -------------------------------------------------------------------- */
        Feature  poFeature;
        int         nFeaturesInTransaction = 0;
        long        nCount = 0;

        int iSrcZField = -1;
        if (pszZField != null)
        {
            iSrcZField = poSrcFDefn.GetFieldIndex(pszZField);
        }

        poSrcLayer.ResetReading();

        if( nGroupTransactions > 0)
            poDstLayer.StartTransaction();

        while( true )
        {
            Feature      poDstFeature = null;

            if( nFIDToFetch != OGRNullFID )
            {
                // Only fetch feature on first pass.
                if( nFeaturesInTransaction == 0 )
                    poFeature = poSrcLayer.GetFeature(nFIDToFetch);
                else
                    poFeature = null;
            }
            else
                poFeature = poSrcLayer.GetNextFeature();

            if( poFeature == null )
                break;

            int nParts = 0;
            int nIters = 1;
            if (bExplodeCollections)
            {
                Geometry poSrcGeometry = poFeature.GetGeometryRef();
                if (poSrcGeometry != null)
                {
                    switch (wkbFlatten((int)poSrcGeometry.GetGeometryType()))
                    {
                        case (int)wkbGeometryType.wkbMultiPoint:
                        case (int)wkbGeometryType.wkbMultiLineString:
                        case (int)wkbGeometryType.wkbMultiPolygon:
                        case (int)wkbGeometryType.wkbGeometryCollection:
                            nParts = poSrcGeometry.GetGeometryCount();
                            nIters = nParts;
                            if (nIters == 0)
                                nIters = 1;
                            break;
                    }
                }
            }

            for(int iPart = 0; iPart < nIters; iPart++)
            {

                if( ++nFeaturesInTransaction == nGroupTransactions )
                {
                    poDstLayer.CommitTransaction();
                    poDstLayer.StartTransaction();
                    nFeaturesInTransaction = 0;
                }

                Gdal.ErrorReset();
                poDstFeature = new Feature( poDstLayer.GetLayerDefn() );

                if( poDstFeature.SetFromWithMap( poFeature, 1, panMap ) != 0 )
                {
                    if( nGroupTransactions > 0)
                        poDstLayer.CommitTransaction();

                    Console.Error.Write(
                            "Unable to translate feature " + poFeature.GetFID() + " from layer " +
                            poSrcFDefn.GetName() );

                    poFeature.Dispose();
                    poFeature = null;
                    poDstFeature.Dispose();
                    poDstFeature = null;
                    return false;
                }

                if( bPreserveFID )
                    poDstFeature.SetFID( poFeature.GetFID() );

                Geometry poDstGeometry = poDstFeature.GetGeometryRef();
                if (poDstGeometry != null)
                {
                    if (nParts > 0)
                    {
                        /* For -explodecollections, extract the iPart(th) of the geometry */
                        Geometry poPart = poDstGeometry.GetGeometryRef(iPart).Clone();
                        poDstFeature.SetGeometryDirectly(poPart);
                        poDstGeometry = poPart;
                    }

                    if (iSrcZField != -1)
                    {
                        SetZ(poDstGeometry, poFeature.GetFieldAsDouble(iSrcZField));
                        /* This will correct the coordinate dimension to 3 */
                        Geometry poDupGeometry = poDstGeometry.Clone();
                        poDstFeature.SetGeometryDirectly(poDupGeometry);
                        poDstGeometry = poDupGeometry;
                    }

                    if (eGeomOp == GeomOperation.SEGMENTIZE)
                    {
                /*if (poDstFeature.GetGeometryRef() != null && dfGeomOpParam > 0)
                    poDstFeature.GetGeometryRef().segmentize(dfGeomOpParam);*/
                    }
                    else if (eGeomOp == GeomOperation.SIMPLIFY_PRESERVE_TOPOLOGY && dfGeomOpParam > 0)
                    {
                        Geometry poNewGeom = poDstGeometry.SimplifyPreserveTopology(dfGeomOpParam);
                        if (poNewGeom != null)
                        {
                            poDstFeature.SetGeometryDirectly(poNewGeom);
                            poDstGeometry = poNewGeom;
                        }
                    }

                    if (poClipSrc != null)
                    {
                        Geometry poClipped = poDstGeometry.Intersection(poClipSrc);
                        if (poClipped == null || poClipped.IsEmpty())
                        {
                            /* Report progress */
                            nCount ++;
                            if (pfnProgress != null)
                                pfnProgress.run(nCount * 1.0 / nCountLayerFeatures, "");
                            poDstFeature.Dispose();
                            continue;
                        }
                        poDstFeature.SetGeometryDirectly(poClipped);
                        poDstGeometry = poClipped;
                    }

                    if( poCT != null )
                    {
                        eErr = poDstGeometry.Transform( poCT );
                        if( eErr != 0 )
                        {
                            if( nGroupTransactions > 0)
                                poDstLayer.CommitTransaction();

                            Console.Error.Write("Failed to reproject feature" + poFeature.GetFID() + " (geometry probably out of source or destination SRS).");
                            if( !bSkipFailures )
                            {
                                poFeature.Dispose();
                                poFeature = null;
                                poDstFeature.Dispose();
                                poDstFeature = null;
                                return false;
                            }
                        }
                    }
                    else if (poOutputSRS != null)
                    {
                        poDstGeometry.AssignSpatialReference(poOutputSRS);
                    }

                    if (poClipDst != null)
                    {
                        Geometry poClipped = poDstGeometry.Intersection(poClipDst);
                        if (poClipped == null || poClipped.IsEmpty())
                        {
                            /* Report progress */
                            nCount ++;
                            if (pfnProgress != null)
                                pfnProgress.run(nCount * 1.0 / nCountLayerFeatures, "");
                            poDstFeature.Dispose();
                            continue;
                        }
                        poDstFeature.SetGeometryDirectly(poClipped);
                        poDstGeometry = poClipped;
                    }

                    if( bForceToPolygon )
                    {
                        poDstFeature.SetGeometryDirectly(Ogr.ForceToPolygon(poDstGeometry));
                    }

                    else if( bForceToMultiPolygon )
                    {
                        poDstFeature.SetGeometryDirectly(Ogr.ForceToMultiPolygon(poDstGeometry));
                    }

                    else if ( bForceToMultiLineString )
                    {
                        poDstFeature.SetGeometryDirectly(Ogr.ForceToMultiLineString(poDstGeometry));
                    }
                }

                Gdal.ErrorReset();
                if( poDstLayer.CreateFeature( poDstFeature ) != 0
                    && !bSkipFailures )
                {
                    if( nGroupTransactions > 0 )
                        poDstLayer.RollbackTransaction();

                    poDstFeature.Dispose();
                    poDstFeature = null;
                    return false;
                }

                poDstFeature.Dispose();
                poDstFeature = null;
            }

            poFeature.Dispose();
            poFeature = null;

            /* Report progress */
            nCount ++;
            if (pfnProgress != null)
                pfnProgress.run(nCount * 1.0 / nCountLayerFeatures, "");

        }

        if( nGroupTransactions > 0 )
            poDstLayer.CommitTransaction();

        return true;
    }
}