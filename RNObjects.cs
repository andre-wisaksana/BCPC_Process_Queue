public class ParentChild
{
    public string ID { get; set; } = string.Empty;
    public string incident_type { get; set; } = string.Empty;
    public string contact_type { get; set; } = string.Empty;
    public string contact_file_field { get; set; } = string.Empty;
    public string org_file_field { get; set; } = string.Empty;
    public string plan_file_field { get; set; } = string.Empty;
    public string bus_event_file_field { get; set; } = string.Empty;
    public string cf2inherit { get; set; } = string.Empty;
    public string header { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string config_name { get; set; } = string.Empty;
    public string config_value { get; set; } = string.Empty;
    public string exclude_query { get; set; } = string.Empty;

}


public class QueueItem
{
    public string ID { get; set; } = string.Empty;

    // NamedID for Incident to be created
    public string Incident { get; set; } = string.Empty;

    // NamedID for ParentChild - the config that we go against
    public string ParentChild { get; set; } = string.Empty;

    // This is the data string - we need to run the CSV parser on this
    public string DataString { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ParentIncident { get; set; } = string.Empty;
    public string debugCONTACT { get; set; } = string.Empty;
    public string debugORG { get; set; } = string.Empty;
    public string debugPLAN { get; set; } = string.Empty;
    public string debugMBRTYPE { get; set; } = string.Empty;
    public string debugBE { get; set; } = string.Empty;
    public string StatusNote { get; set; } = string.Empty;
    public string bcpc_BE_number { get; set; } = string.Empty;
}