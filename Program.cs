using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Reflection;

namespace BCPC_Process_Queue
{



    class Program
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static void Main(string[] args)
        {

            // This job will only process a fix number of queued items as indicated in the 
            log.Info("Start");
            SOAPConnect connect = new SOAPConnect();

            try
            {
                connect.getData();

                var parentIncidents = connect._queueItems.GroupBy(x => x.ParentIncident);

                // we now go through the parent incidents
                foreach (var parentIncident in parentIncidents)
                {
                    var parentincidentid = parentIncident.Key;

                    connect.updateQueueItems(parentincidentid, "In Process", "New", "");

                    connect.fetchParent(parentincidentid);
                    // we get the queue items that we need to handle

                    // we should loop through the templates
                    var configByParentIncident = connect._queueItems.FindAll(x => x.ParentIncident == parentincidentid).GroupBy(x => x.ParentChild);

                    foreach (var config in configByParentIncident)
                    {
                        connect.getExclusions(parentincidentid, config.Key);
                        var item2process = connect._queueItems.FindAll(x => x.ParentIncident == parentincidentid && x.ParentChild == config.Key);

                        foreach (var item in item2process)
                        {

                            var result = connect.excludedContactList.FirstOrDefault(x => x == item.debugCONTACT);

                            if (result == null)
                                connect.createIncident(item.ID);
                            else
                            {
                                item.Status = "Exclusion";
                                item.StatusNote = "Matched exculsion criteria";
                            }

                        }
                    }

                    connect.updateQueueItems(parentincidentid, "Complete", "In Process", "");

                }
            } catch (Exception e)
            {
                log.Error("Exception in main program", e);                
            }


            log.Info("End");



        }
    }
}
