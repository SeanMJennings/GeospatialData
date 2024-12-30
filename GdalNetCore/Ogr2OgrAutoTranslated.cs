/******************************************************************************
 *
 * Project:  OpenGIS Simple Features Reference Implementation
 * Purpose:  C# port of a simple client for translating between formats.
 * Author:   Even Rouault, <even dot rouault at spatialys.com>
 *
 * Port from ogr2ogr.cpp by Frank Warmerdam
 *
 ******************************************************************************
 * Copyright (c) 2009, Even Rouault
 * Copyright (c) 1999, Frank Warmerdam
 *
 * SPDX-License-Identifier: MIT
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using OSGeo.GDAL;
using OSGeo.OGR;
using OSGeo.OSR;

class GDALScaledProgress : ProgressCallback
{
    private double pctMin;
    private double pctMax;
    private ProgressCallback mainCbk;

    public GDALScaledProgress(double pctMin, double pctMax, ProgressCallback mainCbk)
    {
        this.pctMin = pctMin;
        this.pctMax = pctMax;
        this.mainCbk = mainCbk;
    }

    public int Run(double dfComplete, string message)
    {
        return mainCbk.Run(pctMin + dfComplete * (pctMax - pctMin), message);
    }
}

public class ogr2ogr
{
    static bool bSkipFailures = false;
    static int nGroupTransactions = 200;
    static bool bPreserveFID = false;
    static readonly int OGRNullFID = -1;
    static int nFIDToFetch = OGRNullFID;

    static class GeomOperation
    {
        private GeomOperation() { }
        public static GeomOperation NONE = new GeomOperation();
        public static GeomOperation SEGMENTIZE = new GeomOperation();
        public static GeomOperation SIMPLIFY_PRESERVE_TOPOLOGY = new GeomOperation();
    }

    /************************************************************************/
    /*                                main()                                */
    /************************************************************************/

    public static void Main(string[] args)
    {
        string pszFormat = "ESRI Shapefile";
        string pszDataSource = null;
        string pszDestDataSource = null;
        List<string> papszLayers = new List<string>();
        List<string> papszDSCO = new List<string>(), papszLCO = new List<string>();
        bool bTransform = false;
        bool bAppend = false, bUpdate = false, bOverwrite = false;
        string pszOutputSRSDef = null;
        string pszSourceSRSDef = null;
        SpatialReference poOutputSRS = null;
        SpatialReference poSourceSRS = null;
        string pszNewLayerName = null;
        string pszWHERE = null;
        Geometry poSpatialFilter = null;
        string pszSelect;
        List<string> papszSelFields = null;
        string pszSQLStatement = null;
        int eGType = -2;
        GeomOperation eGeomOp = GeomOperation.NONE;
        double dfGeomOpParam = 0;
        List<string> papszFieldTypesToString = new List<string>();
        bool bDisplayProgress = false;
        ProgressCallback pfnProgress = null;
        bool bClipSrc = false;
        Geometry poClipSrc = null;
        string pszClipSrcDS = null;
        string pszClipSrcSQL = null;
        string pszClipSrcLayer = null;
        string pszClipSrcWhere = null;
        Geometry poClipDst = null;
        string pszClipDstDS = null;
        string pszClipDstSQL = null;
        string pszClipDstLayer = null;
        string pszClipDstWhere = null;
        string pszSrcEncoding = null;
        string pszDstEncoding = null;
        bool bExplodeCollections = false;
        string pszZField = null;

        ogr.DontUseExceptions();

        /* -------------------------------------------------------------------- */
        /*      Register format(s).                                             */
        /* -------------------------------------------------------------------- */
        if (ogr.GetDriverCount() == 0)
            ogr.RegisterAll();

        /* -------------------------------------------------------------------- */
        /*      Processing command line arguments.                              */
        /* -------------------------------------------------------------------- */
        args = ogr.GeneralCmdLineProcessor(args);

        if (args.Length < 2)
        {
            Usage();
            Environment.Exit(-1);
        }

        for (int iArg = 0; iArg < args.Length; iArg++)
        {
            if (args[iArg].Equals("-f", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszFormat = args[++iArg];
            }
            else if (args[iArg].Equals("-dsco", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                papszDSCO.Add(args[++iArg]);
            }
            else if (args[iArg].Equals("-lco", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                papszLCO.Add(args[++iArg]);
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
                nFIDToFetch = int.Parse(args[++iArg]);
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
                    eGType = ogr.wkbNone;
                else if (args[iArg + 1].Equals("GEOMETRY", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbUnknown;
                else if (args[iArg + 1].Equals("POINT", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbPoint;
                else if (args[iArg + 1].Equals("LINESTRING", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbLineString;
                else if (args[iArg + 1].Equals("POLYGON", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbPolygon;
                else if (args[iArg + 1].Equals("GEOMETRYCOLLECTION", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbGeometryCollection;
                else if (args[iArg + 1].Equals("MULTIPOINT", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbMultiPoint;
                else if (args[iArg + 1].Equals("MULTILINESTRING", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbMultiLineString;
                else if (args[iArg + 1].Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbMultiPolygon;
                else if (args[iArg + 1].Equals("GEOMETRY25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbUnknown | ogr.wkb25DBit;
                else if (args[iArg + 1].Equals("POINT25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbPoint25D;
                else if (args[iArg + 1].Equals("LINESTRING25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbLineString25D;
                else if (args[iArg + 1].Equals("POLYGON25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbPolygon25D;
                else if (args[iArg + 1].Equals("GEOMETRYCOLLECTION25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbGeometryCollection25D;
                else if (args[iArg + 1].Equals("MULTIPOINT25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbMultiPoint25D;
                else if (args[iArg + 1].Equals("MULTILINESTRING25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbMultiLineString25D;
                else if (args[iArg + 1].Equals("MULTIPOLYGON25D", StringComparison.OrdinalIgnoreCase))
                    eGType = ogr.wkbMultiPolygon25D;
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
                Geometry oRing = new Geometry(ogrConstants.wkbLinearRing);
                double xmin = double.Parse(args[++iArg]);
                double ymin = double.Parse(args[++iArg]);
                double xmax = double.Parse(args[++iArg]);
                double ymax = double.Parse(args[++iArg]);
                oRing.AddPoint(xmin, ymin);
                oRing.AddPoint(xmin, ymax);
                oRing.AddPoint(xmax, ymax);
                oRing.AddPoint(xmax, ymin);
                oRing.AddPoint(xmin, ymin);

                poSpatialFilter = new Geometry(ogrConstants.wkbPolygon);
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
            else if (args[iArg].Equals("-clipsrc", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                bClipSrc = true;
                if (IsNumber(args[iArg + 1]) && iArg < args.Length - 4)
                {
                    Geometry oRing = new Geometry(ogrConstants.wkbLinearRing);
                    double xmin = double.Parse(args[++iArg]);
                    double ymin = double.Parse(args[++iArg]);
                    double xmax = double.Parse(args[++iArg]);
                    double ymax = double.Parse(args[++iArg]);
                    oRing.AddPoint(xmin, ymin);
                    oRing.AddPoint(xmin, ymax);
                    oRing.AddPoint(xmax, ymax);
                    oRing.AddPoint(xmax, ymin);
                    oRing.AddPoint(xmin, ymin);

                    poClipSrc = new Geometry(ogrConstants.wkbPolygon);
                    poClipSrc.AddGeometry(oRing);
                }
                else if ((args[iArg + 1].Length >= 7 && args[iArg + 1].Substring(0, 7).Equals("POLYGON", StringComparison.OrdinalIgnoreCase)) ||
                         (args[iArg + 1].Length >= 12 && args[iArg + 1].Substring(0, 12).Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase)))
                {
                    poClipSrc = Geometry.CreateFromWkt(args[iArg + 1]);
                    if (poClipSrc == null)
                    {
                        Console.Error.Write("FAILURE: Invalid geometry. Must be a valid POLYGON or MULTIPOLYGON WKT\n\n");
                        Usage();
                    }
                    iArg++;
                }
                else if (args[iArg + 1].Equals("spat_extent", StringComparison.OrdinalIgnoreCase))
                {
                    iArg++;
                }
                else
                {
                    pszClipSrcDS = args[iArg + 1];
                    iArg++;
                }
            }
            else if (args[iArg].Equals("-clipsrcsql", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipSrcSQL = args[iArg + 1];
                iArg++;
            }
            else if (args[iArg].Equals("-clipsrclayer", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipSrcLayer = args[iArg + 1];
                iArg++;
            }
            else if (args[iArg].Equals("-clipsrcwhere", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipSrcWhere = args[iArg + 1];
                iArg++;
            }
            else if (args[iArg].Equals("-clipdst", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                if (IsNumber(args[iArg + 1]) && iArg < args.Length - 4)
                {
                    Geometry oRing = new Geometry(ogrConstants.wkbLinearRing);
                    double xmin = double.Parse(args[++iArg]);
                    double ymin = double.Parse(args[++iArg]);
                    double xmax = double.Parse(args[++iArg]);
                    double ymax = double.Parse(args[++iArg]);
                    oRing.AddPoint(xmin, ymin);
                    oRing.AddPoint(xmin, ymax);
                    oRing.AddPoint(xmax, ymax);
                    oRing.AddPoint(xmax, ymin);
                    oRing.AddPoint(xmin, ymin);

                    poClipDst = new Geometry(ogrConstants.wkbPolygon);
                    poClipDst.AddGeometry(oRing);
                }
                else if ((args[iArg + 1].Length >= 7 && args[iArg + 1].Substring(0, 7).Equals("POLYGON", StringComparison.OrdinalIgnoreCase)) ||
                         (args[iArg + 1].Length >= 12 && args[iArg + 1].Substring(0, 12).Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase)))
                {
                    poClipDst = Geometry.CreateFromWkt(args[iArg + 1]);
                    if (poClipDst == null)
                    {
                        Console.Error.Write("FAILURE: Invalid geometry. Must be a valid POLYGON or MULTIPOLYGON WKT\n\n");
                        Usage();
                    }
                    iArg++;
                }
                else if (args[iArg + 1].Equals("spat_extent", StringComparison.OrdinalIgnoreCase))
                {
                    iArg++;
                }
                else
                {
                    pszClipDstDS = args[iArg + 1];
                    iArg++;
                }
            }
            else if (args[iArg].Equals("-clipdstsql", StringComparison.OrdinalIgnoreCase) && iArg < args.Length - 1)
            {
                pszClipDstSQL = args[iArg + 1];
                iArg++;
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

        if (pszDataSource == null)
            Usage();

        if (bPreserveFID && bExplodeCollections)
        {
            Console.Error.Write("FAILURE: cannot use -preserve_fid and -explodecollections at the same time\n\n");
            Usage();
        }

        if (bClipSrc && pszClipSrcDS != null)
        {
            poClipSrc = LoadGeometry(pszClipSrcDS, pszClipSrcSQL, pszClipSrcLayer, pszClipSrcWhere);
            if (poClipSrc == null)
            {
                Console.Error.Write("FAILURE: cannot load source clip geometry\n\n");
                Usage();
            }
        }
        else if (bClipSrc && poClipSrc == null)
        {
            if (poSpatialFilter != null)
                poClipSrc = poSpatialFilter.Clone();
            if (poClipSrc == null)
            {
                Console.Error.Write("FAILURE: -clipsrc must be used with -spat option or a\n" +
                                    "bounding box,");
            }
        }
    }

    private static void Usage()
    {
        // Implementation of usage message
    }

    private static bool IsNumber(string value)
    {
        // Implementation to check if the string is a number
        return double.TryParse(value, out _);
    }

    private static Geometry LoadGeometry(string dataSource, string sql, string layer, string where)
    {
        // Implementation to load geometry from the specified data source
        return null; // Placeholder return
    }
}
