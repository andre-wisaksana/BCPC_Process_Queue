using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;

// Added references
using System.Data;
using System.IO;
using System.Collections;
using BCPC_Process_Queue.RightNowService;
using System.Configuration;
using System.ServiceModel;
using System.Web.Services.Protocols;


namespace BCPC_Process_Queue
{

    class SOAPConnect
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // this is our working limit        
        int limit = Convert.ToInt32(ConfigurationManager.AppSettings["limit"]);
        string token = string.Empty;

        DateTime startRun = new DateTime();

        RightNowSyncPortClient _client;

        // One time gets
        public List<ParentChild> _configRecords = new List<ParentChild>();
        public List<QueueItem> _queueItems = new List<QueueItem>();

        List<NamedID> planList = new List<NamedID>();
        List<NamedID> member_typeList = new List<NamedID>();
        List<NamedID> incident_typeList = new List<NamedID>();


        // Part of parent incident enumeration so needs to be reset per iteration
        public List<string> excludedContactList = new List<string>();
        public List<string> excludedIncidentList = new List<string>();

        Incident parentIncident;  // this is the parent incident

        public int retryMax = 10;
        public int retry = 0;

        // 1. Constructor
        public SOAPConnect()
        {
            _client = new RightNowSyncPortClient();
            _client.ClientCredentials.UserName.UserName = (string)ConfigurationManager.AppSettings["uname"];
            _client.ClientCredentials.UserName.Password = (string)ConfigurationManager.AppSettings["pw"];

            log.Info("SOAPConnect");
        }


        // 2. Get Data - this consists of configuration and queue data
        public void getData()
        {
            // we don't limit because the chances of us having 10k config records is nigh zero
            string query = "select ID, incident_type, contact_type, contact_file_field, org_file_field, plan_file_field, bus_event_file_field, cf2inherit, header, name, config_name, config_value, exclude_query from bcpc.ParentChild";

            query += ";";

            // note we apply the limit here
            query += " select ID as ID, Incident as Incident, ParentChild as ParentChild, DataString as DataString, Status.LookupName as Status, ParentIncident as ParentIncident, debugCONTACT as debugCONTACT, debugORG as debugORG, debugPLAN as debugPLAN, debugMBRTYPE as debugMEMBRTYPE, debugBE as debugBE, StatusNote as StatusNote, bcpc_BE_number as bcpc_BE_number from  bcpc.ImportQueue where (Status.LookupName = 'New' or Status.LookupName = 'In Process') limit " + limit;

            ClientInfoHeader clientInfoHeader = new ClientInfoHeader();
            clientInfoHeader.AppID = "Process Queue - Get Configuration";

            APIAccessRequestHeader apiAccessRequestHeader = new APIAccessRequestHeader();
            apiAccessRequestHeader.Token = token;


            byte[] byteArray;
            CSVTableSet csvtableset = new CSVTableSet();

            APIAccessResponseHeaderType csvResponse = _client.QueryCSV(clientInfoHeader, apiAccessRequestHeader, query, 10000, ",", false, true, out csvtableset, out byteArray);

            token = csvResponse.Token;
            if (csvResponse.NextRequestAfter > 0)
                System.Threading.Thread.Sleep(Convert.ToInt32(csvResponse.NextRequestAfter));

            CSVTable[] csvTables = csvtableset.CSVTables;

            // 1st table is config
            foreach (string row in csvTables[0].Rows)
            {
                string temp = row.Replace("\\\"","\"")+",IGNORE";
                DataTable rowTable = csvParser.Parse(temp, false);

                ParentChild rec = new ParentChild();
                rec.ID = (string)rowTable.Rows[0][0];
                rec.incident_type = (string)rowTable.Rows[0][1];
                rec.contact_type = (string)rowTable.Rows[0][2];
                rec.contact_file_field = (string)rowTable.Rows[0][3];
                rec.org_file_field = (string)rowTable.Rows[0][4];
                rec.plan_file_field = (string)rowTable.Rows[0][5];
                rec.bus_event_file_field = (string)rowTable.Rows[0][6];
                rec.cf2inherit = (string)rowTable.Rows[0][7];
                rec.header = (string)rowTable.Rows[0][8];
                rec.name = (string)rowTable.Rows[0][9];
                rec.config_name = (string)rowTable.Rows[0][10];
                rec.config_value = (string)rowTable.Rows[0][11];
                rec.exclude_query = (string)rowTable.Rows[0][12];

                _configRecords.Add(rec);
            }


            // second table is import queue
            foreach (string row in csvTables[1].Rows)
            {
                string temp = row+ ",IGNORE";

                DataTable rowTable = csvParser.Parse(temp, false);

                QueueItem rec = new QueueItem();
                rec.ID = (string)rowTable.Rows[0][0];
                rec.Incident = (string)rowTable.Rows[0][1];
                rec.ParentChild = (string)rowTable.Rows[0][2];
                rec.DataString = (string)rowTable.Rows[0][3];
                rec.Status = (string)rowTable.Rows[0][4];
                rec.ParentIncident = (string)rowTable.Rows[0][5];
                rec.debugCONTACT = (string)rowTable.Rows[0][6];
                rec.debugORG = (string)rowTable.Rows[0][7];
                rec.debugPLAN = (string)rowTable.Rows[0][8];
                rec.debugMBRTYPE = (string)rowTable.Rows[0][9];
                rec.debugBE = (string)rowTable.Rows[0][10];
                rec.StatusNote = (string)rowTable.Rows[0][11];
                rec.bcpc_BE_number = (string)rowTable.Rows[0][12];

                _queueItems.Add(rec);
            }

            log.Info("getData");

            // 20221201 Getting named IDs was part of the original implementation
            // we can actually specify namedid by name so we're going to try that for this round
            //planList = getNamedIDs("Incident.CustomFields.c.plan");
            //member_typeList = getNamedIDs("Incident.CustomFields.c.member_type");
            //incident_typeList = getNamedIDs("Incident.CustomFields.c.incident_type");
        }


        // 3. We get a list of exclusions
        // we want to query for exclusions - to do so, we do a select
        // 
        public void getExclusions(string parentincident,string parentchild)
        {

            excludedContactList.Clear();
            excludedIncidentList.Clear();

            var itemsToProcess = _queueItems.FindAll(x => x.ParentIncident == parentincident);

            if (itemsToProcess.Count == 0)
                return ;

            // if we are here, we have at least one - though we really should never have zero items to process if we are here

            ParentChild configRecord = _configRecords.FirstOrDefault(x => x.ID == parentchild);

            // we don't want to continue if we have no exclusions
            if (configRecord.exclude_query.Length == 0)
                return;

            // we have a list of queued items
            // we build the list of contacts

            List<string> queueContactList = new List<string>();

            // we get a list
            // we do this list approach because we may have a single contact 
            // IMPORTANT - we are getting contacts on a config record basis
            // we do this to get economies of scale when querying for exclusions
            foreach (var queue in _queueItems.FindAll(x => x.ParentChild == configRecord.ID))
            {
                queueContactList.Add(queue.debugCONTACT);
            }

            queueContactList = queueContactList.Distinct().ToList();

            // now we build the query
            string contacts = string.Empty;
            foreach (var queue in queueContactList)
            {
                contacts += "" + queue + ",";
            }

            // we must always have a contact
            contacts = contacts.TrimEnd(',');

            // we are now ready to do our exclusion query
            string query = "select count(ID) as NumIncidentID, PrimaryContact.ParentContact.ID as ContactID from Incident where PrimaryContact.ParentContact.ID in (" + contacts + ") ";

            if (configRecord.exclude_query.Length>0)
                query += "and "+configRecord.exclude_query;

            ClientInfoHeader clientInfoHeader = new ClientInfoHeader();
            clientInfoHeader.AppID = "Get Exclusions";

            APIAccessRequestHeader apiAccessRequestHeader = new APIAccessRequestHeader();
            apiAccessRequestHeader.Token = token;

            byte[] byteArray;
            CSVTableSet csvtableset = new CSVTableSet();

            APIAccessResponseHeaderType csvResponse = _client.QueryCSV(clientInfoHeader, apiAccessRequestHeader, query, 10000, ",", false, true, out csvtableset, out byteArray);

            token = csvResponse.Token;
            if (csvResponse.NextRequestAfter > 0)
                System.Threading.Thread.Sleep(Convert.ToInt32(csvResponse.NextRequestAfter));

            CSVTable[] csvTables = csvtableset.CSVTables;

            // 1st table is config
            foreach (string row in csvTables[0].Rows)
            {
                string[] pieces = row.Split(',');
                excludedIncidentList.Add(pieces[0]);
                excludedContactList.Add(pieces[1]);
            }

            log.Info("getExclusions");

        }


        // 4. Fetch Parent
        public void fetchParent(string parentincident)
        {
            var queueItemRecord = _queueItems.FirstOrDefault(x => x.ParentIncident == parentincident);
            var configRecord = _configRecords.FirstOrDefault(x => x.ID == queueItemRecord.ParentChild);

            ClientInfoHeader clientInfoHeader = new ClientInfoHeader();
            clientInfoHeader.AppID = "Process Queue - Get Parent Incident";

            APIAccessRequestHeader apiAccessRequestHeader = new APIAccessRequestHeader();
            apiAccessRequestHeader.Token = token;

            parentIncident = new Incident();
            parentIncident.ID = new ID();
            parentIncident.ID.id = Convert.ToInt64(parentincident);
            parentIncident.ID.idSpecified = true;


            List<GenericField> incidentCustField_gfList = new List<GenericField>();

            // We specify the incidents custom fields, ie, CustomFields.c
            List<GenericField> gfList = new List<GenericField>();

            // incident type          

            gfList.Add(createGenericField("incident_type", ItemsChoiceType.NamedIDValue, null));

            gfList.Add(createGenericField("wf_be_id", ItemsChoiceType.NamedIDValue, null));

            gfList.Add(createGenericField("plan", ItemsChoiceType.NamedIDValue, null));
            
            gfList.Add(createGenericField("member_type", ItemsChoiceType.NamedIDValue, null));
            
            gfList.Add(createGenericField("org_id", ItemsChoiceType.IntegerValue, 0));
            
            foreach (var f in configRecord.cf2inherit.Split(',').ToList())
            {
                gfList.Add(createGenericField(f, ItemsChoiceType.IntegerValue, 0));
            }



            // This is the part where we put the generic fields associated with the custom field into a generic object that representes the c
            // FYI the generic object c is then associated with a generic field c 
            GenericObject customfield_c_object = new GenericObject();
            customfield_c_object.ObjectType = new RNObjectType();
            customfield_c_object.ObjectType.TypeName = "IncidentCustomFieldsc";
            customfield_c_object.GenericFields = gfList.ToArray();

            GenericField customfield_c = new GenericField();
            customfield_c.name = "c";
            customfield_c.dataType = DataTypeEnum.OBJECT;
            customfield_c.dataTypeSpecified = true;
            customfield_c.DataValue = new DataValue();
            customfield_c.DataValue.Items = new object[1] { customfield_c_object };
            customfield_c.DataValue.ItemsElementName = new ItemsChoiceType[1] { ItemsChoiceType.ObjectValue };

            if (gfList.Count > 0)
                incidentCustField_gfList.Add(customfield_c);

            parentIncident.CustomFields = new GenericObject();
            parentIncident.CustomFields.ObjectType = new RNObjectType();
            parentIncident.CustomFields.ObjectType.TypeName = "IncidentCustomFields";
            parentIncident.CustomFields.GenericFields = incidentCustField_gfList.ToArray();


            GetProcessingOptions processingOptions = new GetProcessingOptions();
            processingOptions.FetchAllNames = false;

            RNObject[] output;

            retry = 0;

            while (retry < retryMax)
                try
                {
                    var response = _client.Get(clientInfoHeader, apiAccessRequestHeader, new RNObject[1] { parentIncident }, processingOptions, out output);
                    // we are only doing 1 get so we know it has to be this
                    parentIncident = (Incident)output[0];
                    token = response.Token;
                    if (response.NextRequestAfter > 0)
                        System.Threading.Thread.Sleep(Convert.ToInt32(response.NextRequestAfter));
                    break;

                }
                catch (FaultException<APIAccessErrorFaultType> fault)
                {                  
                    if (fault.Message.ToLower().Replace(" ", "") != "serverbusy")
                        throw new Exception("Typed Exception from fetchParent, " + fault.Message);

                    if (retry == retryMax)
                        throw new Exception("Typed Exception from fetchParent, exceeded retry limit");

                    retry++;
                    System.Threading.Thread.Sleep(Convert.ToInt32(fault.Detail.NextRequestAfter));
                    token = fault.Detail.Token;
                    log.Info("Retry fetchParent");
                }

            log.Info("fetchParent");

        }


        // 5. Create Incident
        public void createIncident (string qitem_id)
        {
            var queueItem = _queueItems.First(x => x.ID == qitem_id);

            ParentChild configRecord = _configRecords.FirstOrDefault(x => x.ID == queueItem.ParentChild);

            Incident incident = new Incident();


            List<GenericField> incidentCustField_gfList = new List<GenericField>();

            // Contact            
            incident.PrimaryContact = new IncidentContact();
            incident.PrimaryContact.Contact = new NamedID();
            incident.PrimaryContact.Contact.ID = new ID();
            if (queueItem.debugCONTACT.Length>0)
                incident.PrimaryContact.Contact.ID.id = Convert.ToInt64(queueItem.debugCONTACT);
            incident.PrimaryContact.Contact.ID.idSpecified = true;

            // We specify the incidents custom fields, ie, CustomFields.c
            List<GenericField> gfList = new List<GenericField>();

            // incident type          
            if (configRecord.incident_type.Length > 0)
            {
                NamedID incidenttypeNamedID = new NamedID();
                incidenttypeNamedID.Name = configRecord.incident_type;
                gfList.Add(createGenericField("incident_type", ItemsChoiceType.NamedIDValue, incidenttypeNamedID));
            }

            // business event - apply from datatable                
            if (queueItem.debugBE.Length > 0)
            {
                NamedID namedid = new NamedID();
                namedid.ID = new ID();
                namedid.ID.id = Convert.ToInt64(queueItem.debugBE);
                namedid.ID.idSpecified = true;

                if (namedid.ID.id > 0)
                    gfList.Add(createGenericField("wf_be_id", ItemsChoiceType.NamedIDValue, namedid));
            }

            // plan - apply from datatable                
            if (queueItem.debugPLAN.Length > 0)
            {

                NamedID namedid = new NamedID();
                namedid.ID = new ID();
                namedid.ID.id = Convert.ToInt64(queueItem.debugPLAN);
                namedid.ID.idSpecified = true;

                if (namedid.ID.id > 0)
                    gfList.Add(createGenericField("plan", ItemsChoiceType.NamedIDValue, namedid));
            }

            // membertype - apply from datatable                
            if (queueItem.debugMBRTYPE.Length >0)
            {
                NamedID namedid = new NamedID();
                namedid.ID = new ID();
                namedid.ID.id = Convert.ToInt64(queueItem.debugMBRTYPE);
                namedid.ID.idSpecified = true;

                if (namedid.ID.id > 0)
                    gfList.Add(createGenericField("member_type", ItemsChoiceType.NamedIDValue, namedid));
            }

            // orgnaization id- apply from datatable
            // this is the custom field not the custom object
            // the custom object will be set via business rules
            if (queueItem.debugORG.Length > 0)
            {
                gfList.Add(createGenericField("org_id", ItemsChoiceType.IntegerValue, Convert.ToInt32(queueItem.debugORG)));
            }


            // we loop through the generic fields
            foreach (var customField_gf in parentIncident.CustomFields.GenericFields)
            {
                if (customField_gf.name == "c")
                {
                    GenericObject c = (GenericObject)customField_gf.DataValue.Items[0];

                    foreach (var cf in c.GenericFields)
                    {
                        var result = configRecord.cf2inherit.Split(',').ToList().FirstOrDefault(x => x == cf.name);
                        if (result != null)
                        {
                            // the field is in the inherit list so we add it
                            gfList.Add(cf);
                        }
                    }

                }
            }

            // 20221201             
            gfList.Add(createGenericField("m_verification", ItemsChoiceType.NamedIDValue, new NamedID { Name = "Validated" }));

            // This is the part where we put the generic fields associated with the custom field into a generic object that representes the c
            // FYI the generic object c is then associated with a generic field c 
            GenericObject customfield_c_object = new GenericObject();
            customfield_c_object.ObjectType = new RNObjectType();
            customfield_c_object.ObjectType.TypeName = "IncidentCustomFieldsc";
            customfield_c_object.GenericFields = gfList.ToArray();

            GenericField customfield_c = new GenericField();
            customfield_c.name = "c";
            customfield_c.dataType = DataTypeEnum.OBJECT;
            customfield_c.dataTypeSpecified = true;
            customfield_c.DataValue = new DataValue();
            customfield_c.DataValue.Items = new object[1] { customfield_c_object };
            customfield_c.DataValue.ItemsElementName = new ItemsChoiceType[1] { ItemsChoiceType.ObjectValue };

            if (gfList.Count > 0)
                incidentCustField_gfList.Add(customfield_c);


            // custom object package WMS
            gfList = new List<GenericField>();

            NamedID parentIncidentNamedID = new NamedID();
            parentIncidentNamedID.ID = new ID();
            parentIncidentNamedID.ID.id = Convert.ToInt32(parentIncident.ID.id);
            parentIncidentNamedID.ID.idSpecified = true;
            gfList.Add(createGenericField("ParentIncident", ItemsChoiceType.NamedIDValue, parentIncidentNamedID));

            GenericObject customfield_WMS_object = new GenericObject();
            customfield_WMS_object.ObjectType = new RNObjectType();
            customfield_WMS_object.ObjectType.TypeName = "WMS";
            customfield_WMS_object.GenericFields = gfList.ToArray();

            GenericField customfield_WMS = new GenericField();
            customfield_WMS.name = "WMS";
            customfield_WMS.dataType = DataTypeEnum.OBJECT;
            customfield_WMS.dataTypeSpecified = true;
            customfield_WMS.DataValue = new DataValue();
            customfield_WMS.DataValue.Items = new object[1] { customfield_WMS_object };
            customfield_WMS.DataValue.ItemsElementName = new ItemsChoiceType[1] { ItemsChoiceType.ObjectValue };

            if (gfList.Count > 0)
                incidentCustField_gfList.Add(customfield_WMS);


            incident.CustomFields = new GenericObject();
            incident.CustomFields.ObjectType = new RNObjectType();
            incident.CustomFields.ObjectType.TypeName = "IncidentCustomFields";
            incident.CustomFields.GenericFields = incidentCustField_gfList.ToArray();

            RightNowService.Thread incidentthread = new RightNowService.Thread();

            DataTable columns = csvParser.Parse(configRecord.header+",IGNORE");
            DataTable record = csvParser.Parse(queueItem.DataString + ",IGNORE");


            StringBuilder text = new StringBuilder();
            for (int i=0; i< columns.Columns.Count;i++)
            {
                // we skip the first 8 columns as they are internal
                text.AppendLine(columns.Rows[0][i].ToString()+ ": " + record.Rows[0][i].ToString());

            }

            incidentthread.Text = text.ToString();
            incidentthread.action = ActionEnum.add;
            incidentthread.actionSpecified = true;
            incidentthread.EntryType = new NamedID();
            incidentthread.EntryType.ID = new ID();
            incidentthread.EntryType.ID.id = 1;
            incidentthread.EntryType.ID.idSpecified = true;

            incident.Threads = new RightNowService.Thread[1] { incidentthread };

            // we have now finished setting up the data - now we are going to call the actual create

            ClientInfoHeader clientInfoHeader = new ClientInfoHeader();
            clientInfoHeader.AppID = "Process Queue - Create Incident";

            APIAccessRequestHeader apiAccessRequestHeader = new APIAccessRequestHeader();
            apiAccessRequestHeader.Token = token;
            //apiAccessRequestHeader.Token = "";

            CreateProcessingOptions options = new CreateProcessingOptions();
            options.SuppressRules = false;
            options.SuppressExternalEvents = false;

            retry = 0;

            while (retry < retryMax)
                try
                {
                    //throw new Exception("TEST"); for testing
                    RNObject[] output;
                    var response = _client.Create(clientInfoHeader, apiAccessRequestHeader, new RNObject[1] { incident }, options, out output);
                    token = response.Token;
                    if (response.NextRequestAfter > 0)
                        System.Threading.Thread.Sleep(Convert.ToInt32(response.NextRequestAfter));

                    queueItem.Incident = output[0].ID.id.ToString();
                    break;
                }
                catch (FaultException<APIAccessErrorFaultType> fault)
                {
                    if (fault.Message.ToLower().Replace(" ", "") != "serverbusy")
                        throw new Exception("Typed Exception from createIncident, " + fault.Message);

                    if (retry == retryMax)
                        throw new Exception("Typed Exception from createIncident, exceeded retry limit");

                    retry++;
                    System.Threading.Thread.Sleep(Convert.ToInt32(fault.Detail.NextRequestAfter));
                    token = fault.Detail.Token;
                    log.Info("retry createIncident");
                }
                catch (Exception ex)
                {
                    queueItem.StatusNote= ex.Message;
                    queueItem.Status = "Error - RN";
                    break;
                }
            log.Info("createIncident");

        }

        // 99. Queue Status = we use this to handle the items
        // we pass in optional setAllStatusText to allow us to mass set status
        // we pass in newstatus to mass set status - status is mandatory
        // we also update the incident ID if it is set
        // This gets called multiple times
        public void updateQueueItems(string parentincident, string newstatus, string prevstatus, string setAllStatusText)
        {
            var itemsToProcess = _queueItems.FindAll(x => x.ParentIncident == parentincident);

            if (itemsToProcess.Count == 0)
                return;

            // if we are here, we have at least one - though we really should never have zero items to process if we are here

            ParentChild configRecord = _configRecords.FirstOrDefault(x => x.ID == itemsToProcess[0].ParentChild);

            List<GenericObject> _queueItem_GOList = new List<GenericObject>();

            foreach (var item in itemsToProcess)
            {
                GenericObject go = new GenericObject();
                List<GenericField> gfList = new List<GenericField>();

                go.ObjectType = new RNObjectType();
                go.ObjectType.Namespace = "bcpc";
                go.ObjectType.TypeName = "ImportQueue";

                go.ID = new ID();
                go.ID.id = Convert.ToInt32(item.ID);
                go.ID.idSpecified = true;

                // we need to build the custom object 
                NamedID status = new NamedID();

                if (item.Status == prevstatus)
                {
                    item.Status = status.Name = newstatus;
                }
                else
                    status.Name = item.Status;

                gfList.Add(createGenericField("Status", ItemsChoiceType.NamedIDValue, status));



                // we do all first... but then we apply the record's statusnote 
                if (setAllStatusText.Length > 0 && item.StatusNote.Length == 0)
                    gfList.Add(createGenericField("StatusNote", ItemsChoiceType.StringValue, setAllStatusText));

                // this is so we don't overwrite legit status messaging               
                if (item.StatusNote.Length > 0)
                    gfList.Add(createGenericField("StatusNote", ItemsChoiceType.StringValue, item.StatusNote));

                if (item.Incident.Length > 0)
                {
                    // we update the incident record if this is set
                    NamedID incidentID = new NamedID();
                    incidentID.ID = new ID();
                    incidentID.ID.id = Convert.ToInt32(item.Incident);
                    incidentID.ID.idSpecified = true;

                    gfList.Add(createGenericField("Incident", ItemsChoiceType.NamedIDValue, incidentID));
                }

                go.GenericFields = gfList.ToArray();
                _queueItem_GOList.Add(go);
            }


            // now we have a list of items in queue
            // we are ready to update the status record
            IEnumerable<List<GenericObject>> listList = HelperUtilities.splitList(_queueItem_GOList, 750); 
            
            foreach (var list in listList)
            {
                UpdateProcessingOptions updateOptions = new UpdateProcessingOptions();
                updateOptions.SuppressRules = true;
                updateOptions.SuppressExternalEvents = true;
                updateOptions.ReturnExpandedSoapFaults = true;
                updateOptions.ReturnExpandedSoapFaultsSpecified = true;

                ClientInfoHeader clientInfoHeader = new ClientInfoHeader();
                clientInfoHeader.AppID = "Process Queue - Update Queue Item";

                APIAccessRequestHeader aPIAccessRequestHeader = new APIAccessRequestHeader();
                aPIAccessRequestHeader.Token = token;

                retry = 0;

                while (retry < retryMax)
                    try
                    {
                        var response = _client.Update(clientInfoHeader, aPIAccessRequestHeader, list.ToArray(), updateOptions);
                        token = response.Token;
                        if (response.NextRequestAfter > 0)
                            System.Threading.Thread.Sleep(Convert.ToInt32(response.NextRequestAfter));
                        break;
                    }
                    catch (FaultException<APIAccessErrorFaultType> fault)
                    {
                        if (fault.Message.ToLower().Replace(" ", "") != "serverbusy")
                            throw new Exception("Typed Exception from createIncident, " + fault.Message);

                        if (retry == retryMax)
                            throw new Exception("Typed Exception from createIncident, exceeded retry limit");

                        retry++;
                        System.Threading.Thread.Sleep(Convert.ToInt32(fault.Detail.NextRequestAfter));
                        token = fault.Detail.Token;
                        log.Info("retryupdateQueueItems");

                    }
            }
            log.Info("updateQueueItems");
        }


        // 99. Get NamedID
        public List<NamedID> getNamedIDs(string name, bool noaction = true)
        {
            NamedID[] returnList = new NamedID[0] ;

            ClientInfoHeader clientInfoHeader = new ClientInfoHeader();
            clientInfoHeader.AppID = "Process Queue - Get NamedID";

            APIAccessRequestHeader apiAccessRequestHeader = new APIAccessRequestHeader();
            apiAccessRequestHeader.Token = token;
            
            retry = 0;

            while (retry < retryMax)
                try
                {
                    var response = _client.GetValuesForNamedID(clientInfoHeader, apiAccessRequestHeader, null, name, out returnList);
                    token = response.Token;
                    if (response.NextRequestAfter > 0)
                        System.Threading.Thread.Sleep(Convert.ToInt32(response.NextRequestAfter));
                    break;
                }
                catch (FaultException<APIAccessErrorFaultType> fault)
                {
                    if (fault.Message.ToLower().Replace(" ", "") != "serverbusy")
                        throw new Exception("Typed Exception from createIncident, " + fault.Message);

                    if (retry == retryMax)
                        throw new Exception("Typed Exception from createIncident, exceeded retry limit");

                    retry++;
                    System.Threading.Thread.Sleep(Convert.ToInt32(fault.Detail.NextRequestAfter));
                    token = fault.Detail.Token;
                    

                }

            return returnList.ToList<NamedID>();
        }

        

        // 99. Helper method
        private static GenericField createGenericField(string Name, ItemsChoiceType itemsChoiceType, object Value)
        {
            GenericField gf = new GenericField();
            gf.name = Name;

            if (Value == null)
            {
                gf.DataValue = null;
                return gf;
            }

            if (Convert.ToString(Value) == "")
            {
                gf.DataValue = null;
                return gf;
            }

            gf.DataValue = new DataValue();
            gf.DataValue.ItemsElementName = new ItemsChoiceType[] { itemsChoiceType };
            gf.DataValue.Items = new object[] { Value };
            return gf;
        }




    }

}
