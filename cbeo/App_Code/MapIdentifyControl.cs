using System;
using System.Data;
using System.Configuration;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.WebControls.WebParts;
using System.Web.UI.HtmlControls;
using ESRI.ArcGIS.ADF.Web;
using ESRI.ArcGIS.ADF.Web.UI.WebControls;
using ESRI.ArcGIS.ADF.Web.DataSources;
using System.Collections.Specialized;
using ESRI.ArcGIS.ADF.Web.Geometry;
using ESRI.ArcGIS.ADF.Web.Display.Graphics;

namespace WebMapApp
{
    /// <summary>
    /// Summary description for MapIdentifyControl
    /// </summary>
    [ToolboxData("<{0}:MapIdentify runat=server></{0}:MapIdentify>")]
    public class MapIdentify : ESRI.ArcGIS.ADF.Web.UI.WebControls.WebControl
    {
        #region Member variables that can be customized

        /* Column names to exclude from Identify.
         * "Display Column", "GRAPHICS_ID", "IS_SELECTED" and columns of type ADF geometry are already set by the framework to not be visible.
         * GraphicsLayer.GetContentsTemplate honors this visibility setting.*/
        private string[] excludedColumnNames = { "OID", "ObjectID", 
            "#ID#" , 
            "#SHAPE#"};//likely of type IMS geometry

        private string _identifyIconUrl = "images/identify-map-icon.png";
        private string _waitIconUrl = "images/callbackActivityIndicator2.gif";
        private int _identifyTolerance = 2; // tolerance used in identify request... may need to be adjusted to a specific resource type
        private IdentifyOption _idOption = IdentifyOption.VisibleLayers;
        private string _mapBuddyId = "Map1";
        private string _taskResultsId = "TaskResults1";
        private int _numberDecimals = 3; //number of decimals in coordinate string
        private bool _isRTL = false;
        #endregion

        #region Member variables
        private string _id;
        private Map _map;
        #endregion

        public MapIdentify()
        {
        }

        protected override void OnPreRender(EventArgs e)
        {
            base.OnPreRender(e);

            _id = this.ClientID;
            // find the map, task results controls
            _map = Page.FindControl(_mapBuddyId) as Map;

            #region Register script for creating script object
            ScriptManager sm = ScriptManager.GetCurrent(this.Page);
            if (sm != null) sm.RegisterAsyncPostBackControl(this);
            string create = String.Format("\nSys.Application.add_init(function() {{\n\t$create(ESRI.ADF.UI.MapIdentifyTool,{{\"id\":\"{3}\",\"uniqueID\":\"{0}\",\"callbackFunctionString\":\"{1}\",\"identifyIcon\":\"{4}\",\"waitIcon\":\"{5}\"}},null,{{\"map\":\"{2}\"}});\n\tMapIdentifyTool = function() {{ $find('{3}').startIdentify(); }};\n }});\n",
                this.UniqueID, this.CallbackFunctionString, _map.ClientID, this.ClientID, _identifyIconUrl, _waitIconUrl);
            Page.ClientScript.RegisterStartupScript(this.GetType(), this.Id + "_startup", create, true);
            #endregion
        }

        private Control FindControlRecursive(Control root, string id)
        {
            if (root.ID == id)
            {
                return root;
            }
            foreach (Control c in root.Controls)
            {
                Control t = FindControlRecursive(c, id);
                if (t != null)
                {
                    return t;
                }
            }
            return null;
        }

        
        protected override void Render(HtmlTextWriter writer)
        {
            //support script file
            writer.WriteLine("<script type=\"text/javascript\" src=\"JavaScript/MapIdentify.js\"></script>");
        }

        /// <summary>
        /// Identify layers at Point.
        /// </summary>
        /// <param name="map">Map control</param>
        /// <param name="mapPoint">Map point location</param>
        private CallbackResult PointIdentify(Map map, ESRI.ArcGIS.ADF.Web.Geometry.Point mapPoint)
        {
            #region Collections to store results
            System.Collections.ArrayList identifyResults = new System.Collections.ArrayList(); //to send to client
            System.Collections.Generic.List<DataTable> tbList = new System.Collections.Generic.List<DataTable>(); //to store on server for "Add to Results" behavior
            #endregion

            #region Variable declarations
            System.Collections.Generic.Dictionary<string, string> identifyResultItem; //a dictionary to hold info on a result item
            ESRI.ArcGIS.ADF.Web.DataSources.IGISResource resource;
            ESRI.ArcGIS.ADF.Web.DataSources.IQueryFunctionality query;
            System.Data.DataTable[] identifyDataTables = null;
            string tableName;
            string dsName = "";
            System.Data.DataTable identifyTable = null;
            System.Data.DataRowCollection rowCollection = null;
            #endregion

            foreach (ESRI.ArcGIS.ADF.Web.DataSources.IMapFunctionality mapFunc in map.GetFunctionalities())
            {
                if (mapFunc.DisplaySettings.Visible)
                {
                    resource = mapFunc.Resource;
                    query = resource.CreateFunctionality(typeof(ESRI.ArcGIS.ADF.Web.DataSources.IQueryFunctionality), "identify_") as ESRI.ArcGIS.ADF.Web.DataSources.IQueryFunctionality;

                    if ((query != null) && (query.Supports("identify")))
                    {
                        #region Perform the identify query
                        // create a list of layer ids for the identify request
                        string[] layerNames = null;
                        string[] layerIDs = null;
                        query.GetQueryableLayers(null, out layerIDs, out layerNames);
                        try
                        {
                            identifyDataTables = query.Identify(mapFunc.Name, mapPoint, _identifyTolerance, _idOption, layerIDs);
                        }
                        catch
                        {
                            identifyDataTables = null;
                        }
                        #endregion

                        #region Process result tables
                        if (identifyDataTables != null && identifyDataTables.Length > 0)
                        {
                            for (int index = 0; index < identifyDataTables.Length; index++)
                            {
                                identifyTable = identifyDataTables[index];
                                tableName = identifyTable.ExtendedProperties[ESRI.ArcGIS.ADF.Web.Constants.ADFLayerName] as string;
                                if (string.IsNullOrEmpty(tableName))
                                    tableName = identifyDataTables[index].TableName;

                                #region Get template for title and contents for this layer from Map Resource Manager

                                #region Get layer format and apply it
                                string layerID = identifyTable.ExtendedProperties[ESRI.ArcGIS.ADF.Web.Constants.ADFLayerID] as string;
                                LayerFormat layerFormat = null;
                                DataTable formattedTable = identifyTable;
                                if (!string.IsNullOrEmpty(layerID))
                                {
                                    layerFormat = LayerFormat.FromMapResourceManager(map.MapResourceManagerInstance, mapFunc.Resource.Name, layerID);
                                    if (layerFormat != null)
                                    {
                                        ESRI.ArcGIS.ADF.Web.Display.Graphics.GraphicsLayer layer = ESRI.ArcGIS.ADF.Web.Converter.ToGraphicsLayer(
                                            identifyTable, System.Drawing.Color.Empty, System.Drawing.Color.Aqua, System.Drawing.Color.Red, true);
                                        if (layer != null)
                                        {
                                            layerFormat.Apply(layer);
                                            formattedTable = layer;
                                        }
                                    }
                                }
                                #endregion

                                #region Get template for title and contents
                                string contentsTemplate = ESRI.ArcGIS.ADF.Web.Display.Graphics.GraphicsLayer.GetContentsTemplate
                                    (formattedTable, false, System.Drawing.Color.Empty, true, excludedColumnNames);
                                string titleTemplate = ESRI.ArcGIS.ADF.Web.Display.Graphics.GraphicsLayer.GetTitleTemplate(
                                    formattedTable, false);
                                #region Apply templates back to layer format to account for excluded column names
                                if (layerFormat != null && excludedColumnNames.Length > 0)
                                {
                                    layerFormat = layerFormat.Clone() as LayerFormat;//clone layer format so that we do not change the layer format stored in the map resource manager
                                    //exclude column names and get template with field names instead of indices
                                    layerFormat.Contents = ESRI.ArcGIS.ADF.Web.Display.Graphics.GraphicsLayer.GetContentsTemplate(
                                        formattedTable, true, System.Drawing.Color.LightGray, true, excludedColumnNames);
                                }
                                #endregion
                                #endregion
                                #endregion

                                #region For each row in the result layer, create a result item to send to the client, and a result layer to store on the server
                                rowCollection = formattedTable.Rows;
                                if (rowCollection != null && rowCollection.Count > 0)
                                {
                                    double roundFactor = Math.Pow(10, _numberDecimals);
                                    string rtlString = _isRTL ? "&lrm;" : "";
                                    string pointXString = rtlString +  Convert.ToString(Math.Round(mapPoint.X * roundFactor) / roundFactor);
                                    string pointYString = rtlString + Convert.ToString(Math.Round(mapPoint.Y * roundFactor) / roundFactor);
                                    for (int row = 0; row < rowCollection.Count; row++)
                                    {
                                        #region Create a dictionary holding info on this row to send to client
                                        identifyResultItem = new System.Collections.Generic.Dictionary<string, string>();
                                        object[] rowItems = formattedTable.Rows[row].ItemArray;

                                        string title = string.Format(System.Globalization.CultureInfo.InvariantCulture,titleTemplate, rowItems);
                                        string contents = string.Format(System.Globalization.CultureInfo.InvariantCulture,contentsTemplate, rowItems);

                                        #region Bold the title if it does not already contain bold elements
                                        string identifyTitle = title;
                                        if (!identifyTitle.Contains("<B>")
                                            && !identifyTitle.Contains("<b>")
                                            && !identifyTitle.Contains("<STRONG>")
                                            && !identifyTitle.Contains("<strong>"))
                                            identifyTitle = "<b>" + identifyTitle + "</b>";
                                        #endregion

                                        identifyResultItem["title"] = identifyTitle;
                                        identifyResultItem["contents"] = contents;
                                        identifyResultItem["layer"] = tableName;
                                        identifyResultItem["resource"] = mapFunc.Resource.Name;
                                        #endregion

                                        #region Create graphics layer, one per row, to use when "Add to Results" is clicked
                                        //table name is title + table name
                                        string rowName = title + " (" + tableName + ")";
                                        DataTable rowTable = rowToTable(formattedTable.Rows[row], rowName);
                                        #region Convert to graphics layer and Add to graphics dataset, one per row
                                        ESRI.ArcGIS.ADF.Web.Display.Graphics.GraphicsLayer layer = 
                                            ESRI.ArcGIS.ADF.Web.Converter.ToGraphicsLayer(rowTable, System.Drawing.Color.Empty, 
                                            System.Drawing.Color.Aqua, System.Drawing.Color.Red, true);
                                        dsName = String.Format("{0}   {1}, {2}", rowName, pointXString, pointYString);
                                        

                                        //Add dataset for this result item to list of graphics data sets to be stored in session
                                        if (layer != null)
                                        {
                                            layer.RenderOnClient = true;
                                            // Add Display Name
                                            if (!layer.ExtendedProperties.Contains("displayName"))
                                                layer.ExtendedProperties.Add("displayName", dsName);
                                            else
                                                layer.ExtendedProperties["displayName"] = dsName;

                                            if (layerFormat != null)
                                                layerFormat.Apply(layer);
                                            tbList.Add(layer);
                                        }
                                        else
                                        {
                                            // Add Display Name
                                            if (!rowTable.ExtendedProperties.Contains("displayName"))
                                                rowTable.ExtendedProperties.Add("displayName", dsName);
                                            else
                                                rowTable.ExtendedProperties["displayName"] = dsName;

                                            tbList.Add(rowTable);
                                        }
                                        #endregion
                                        #endregion

                                        #region Store result items for each feature
                                        identifyResults.Add(identifyResultItem); //Store each layer feature
                                        #endregion
                                    }
                                }
                                #endregion
                            }
                        }
                        #endregion
                    }
                }
            }
            System.Web.HttpContext.Current.Session.Add("WebAppIdentifyDataTables", tbList.ToArray());

            #region Create callback result to send to client
            //Note: arrays and dictionaries are handled natively by the JSON serialization framework
            return new CallbackResult(this, "mappoint", mapPoint.X, mapPoint.Y, identifyResults);
            #endregion
        }

        private System.Data.DataTable rowToTable(DataRow row, String tableName)
        {
            System.Data.DataTable table = new System.Data.DataTable(tableName);
            DataTable t = row.Table;
            for (int i = 0; i < t.Columns.Count; i++)
            {
                DataColumn c = t.Columns[i];
                DataColumn column = new DataColumn(c.ColumnName, c.DataType);
                table.Columns.Add(column);
            }

            DataRow r = table.NewRow();
            for (int i = 0; i < table.Columns.Count; i++)
            {
                r[i] = row[i];
            }
            table.Rows.Add(r);
            return table;
        }

        /// <summary>
        /// Whether map has at least one resource that supports identify
        /// </summary>
        /// <returns></returns>
        public bool SupportsIdentify()
        {
            ESRI.ArcGIS.ADF.Web.DataSources.IGISResource resource;
            ESRI.ArcGIS.ADF.Web.DataSources.IQueryFunctionality query;
            foreach (ESRI.ArcGIS.ADF.Web.DataSources.IMapFunctionality mapFunc in _map.GetFunctionalities())
            {
                try
                {
                    resource = mapFunc.Resource;
                    query = resource.CreateFunctionality(typeof(ESRI.ArcGIS.ADF.Web.DataSources.IQueryFunctionality), "identify_") as ESRI.ArcGIS.ADF.Web.DataSources.IQueryFunctionality;
                    if ((query != null) && (query.Supports("Identify")))
                        return true;
                }
                catch
                {
                }
            }
            return false;
        }

        private void AddToTaskResults(int index)
        {

            GraphicsDataSet gds = null;
            DataSet ds = null;
            TaskResultNode tr = null;
            string dsName = "No Name Found";
            TaskResults taskResults = Page.FindControl(_taskResultsId) as TaskResults;
           // if TaskResults is nested and the initial search didn't find it, try recursive search... migrated 9.2 apps, customized apps, etc. 
            if (taskResults == null)
                taskResults = FindControlRecursive(Page, _taskResultsId) as TaskResults;
            if (taskResults != null)
            {
                DataTable[] tbList =
                System.Web.HttpContext.Current.Session["WebAppIdentifyDataTables"] as DataTable[];
                DataTable tb = tbList[index];
                if ( tb.ExtendedProperties.Contains("displayName") )
                    dsName = tb.ExtendedProperties["displayName"].ToString();

                if(tb is GraphicsLayer)
                {

                    gds = new GraphicsDataSet();
                    gds.DataSetName = dsName;
                    gds.Tables.Add(tb);
                    tr = taskResults.CreateTaskResultNode(null, null, null, gds, false, true);
                }
                else
                {
                        ds = new DataSet();
                        ds.DataSetName = dsName;
                        ds.Tables.Add(tb);
                        tr = taskResults.CreateTaskResultNode(null, null, null, ds, false, true);
                }
                tr.Expanded = true;
                taskResults.DisplayResults(null, null, null, tr);
                this.CallbackResults.CopyFrom(taskResults.CallbackResults);
            }
        }
        #region Properties

        public string Id
        {
            get { return _id; }
            set { _id = value; }
        }


        /// <summary>
        /// Id of Buddy MapControl
        /// </summary>
        public string MapBuddyId
        {
            get { return _mapBuddyId; }
            set { _mapBuddyId = value; }
        }

        /// <summary>
        /// Id of TaskResults Control
        /// </summary>
        public string TaskResultsId
        {
            get { return _taskResultsId; }
            set { _taskResultsId = value; }
        }

        public int NumberDecimals
        {
            get { return _numberDecimals; }
            set { _numberDecimals = value; }
        }


        #endregion


        #region ICallbackEventHandler Members

        public override string GetCallbackResult()
        {
            string responseString = _callbackArg;
            // break out the responseString into a querystring
            Array keyValuePairs = responseString.Split("&".ToCharArray());
            NameValueCollection m_queryString = new NameValueCollection();
            string[] keyValue;
            if (keyValuePairs.Length > 0)
            {
                for (int i = 0; i < keyValuePairs.Length; i++)
                {
                    keyValue = keyValuePairs.GetValue(i).ToString().Split("=".ToCharArray());
                    m_queryString.Add(keyValue[0], keyValue[1]);
                }
            }
            else
            {
                keyValue = responseString.Split("=".ToCharArray());
                if (keyValue.Length > 0)
                    m_queryString.Add(keyValue[0], keyValue[1]);
            }
            string mode = m_queryString["mode"];
            string dir = m_queryString["dir"];
            if (dir == "rtl") _isRTL = true;
            switch (mode)
            {
                case "identify":
                    Map map = Page.FindControl(this._mapBuddyId) as Map;
                    string xyString = m_queryString["coords"];
                    string[] xy = xyString.Split(char.Parse(":"));
                    ESRI.ArcGIS.ADF.Web.Geometry.Point mapPoint = new Point(double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
                    mapPoint.SpatialReference = map.SpatialReference;
                    CallbackResult cResponse = PointIdentify(map, mapPoint);
                    CallbackResults.Add(cResponse);
                    break;
                case "addresults":
                    string indexString = m_queryString["index"];
                    int index = Convert.ToInt32(indexString);
                    AddToTaskResults(index);
                    break;

            }
            return base.GetCallbackResult();
        }
        #endregion
    }
}