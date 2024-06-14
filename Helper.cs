using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


// Added references
using System.Data;
using System.IO;
using System.Collections;



namespace BCPC_Process_Queue
{

    public class csvParser
    {
        public static DataTable Parse(string data, bool headers)
        {
            return Parse(new StringReader(data), headers);
        }

        public static DataTable ParseComma(string data, bool headers)
        {
            return ParseComma(new StringReader(data), headers);
        }


        public static DataTable Parse(string data)
        {
            return Parse(new StringReader(data));
        }

        public static DataTable Parse(TextReader stream)
        {
            return Parse(stream, false);
        }

        public static DataTable Parse(TextReader stream, bool headers)
        {
            DataTable table = new DataTable();
            CsvStream csv = new CsvStream(stream);
            string[] row = csv.GetNextRow();
            if (row == null)
                return null;
            if (headers)
            {
                foreach (string header in row)
                {
                    if (header != null && header.Length > 0 && !table.Columns.Contains(header))
                        table.Columns.Add(header, typeof(string));
                    else
                        table.Columns.Add(GetNextColumnHeader(table), typeof(string));
                }
                row = csv.GetNextRow();
            }
            while (row != null)
            {
                while (row.Length > table.Columns.Count)
                    table.Columns.Add(GetNextColumnHeader(table), typeof(string));
                table.Rows.Add(row);
                row = csv.GetNextRow();
            }
            return table;
        }

        public static DataTable ParseComma(TextReader stream, bool headers)
        {
            DataTable table = new DataTable();
            CsvStreamComma csv = new CsvStreamComma(stream);
            string[] row = csv.GetNextRow();
            if (row == null)
                return null;
            if (headers)
            {
                foreach (string header in row)
                {
                    if (header != null && header.Length > 0 && !table.Columns.Contains(header))
                        table.Columns.Add(header, typeof(string));
                    else
                        table.Columns.Add(GetNextColumnHeader(table), typeof(string));
                }
                row = csv.GetNextRow();
            }
            while (row != null)
            {
                while (row.Length > table.Columns.Count)
                    table.Columns.Add(GetNextColumnHeader(table), typeof(string));
                table.Rows.Add(row);
                row = csv.GetNextRow();
            }
            return table;
        }


        private static string GetNextColumnHeader(DataTable table)
        {
            int c = 1;
            while (true)
            {
                string h = "Column" + c++;
                if (!table.Columns.Contains(h))
                    return h;
            }
        }
    }

    // Start CSV Stream

    public class CsvStream
    {
        private TextReader stream;

        public CsvStream(TextReader s)
        {
            stream = s;
        }

        public string[] GetNextRow()
        {
            ArrayList row = new ArrayList();
            while (true)
            {
                string item = GetNextItem();
                if (item == null)
                    return row.Count == 0 ? null : (string[])row.ToArray(typeof(string));
                row.Add(item);
            }
        }

        private bool EOS = false;
        private bool EOL = false;

        private string GetNextItem()
        {
            if (EOL)
            {
                // previous item was last in line, start new line
                EOL = false;
                return null;
            }

            bool quoted = false;
            bool predata = true;
            bool postdata = false;
            StringBuilder item = new StringBuilder();

            while (true)
            {
                char c = GetNextChar(true);
                if (EOS)
                    return item.Length > 0 ? item.ToString() : null;

                // 20170715 Update this to also deal with tabs
                if ((postdata || !quoted) && c == ',')
                //if ((postdata || !quoted) && c == '\t')
                    // end of item, return
                    return item.ToString();

                if ((predata || postdata || !quoted) && (c == '\x0A' || c == '\x0D'))
                {
                    // we are at the end of the line, eat newline characters and exit
                    EOL = true;
                    if (c == '\x0D' && GetNextChar(false) == '\x0A')
                        // new line sequence is 0D0A
                        GetNextChar(true);
                    return item.ToString();
                }

                if (predata && c == ' ')
                    // whitespace preceeding data, discard
                    continue;

                // 201211-000003 SPOC synch defect - part 2
                // we need to ignore double-quotes.
                // so we force quoted = false
                if (predata && c == '"')
                {
                    // quoted data is starting
                    quoted = true;
                    predata = false;
                    continue;
                }

                if (predata)
                {
                    // data is starting without quotes
                    predata = false;
                    item.Append(c);
                    continue;
                }

                // 201211-000003 SPOC synch defect - part 2
                // we need to ignore double-quotes.
                // so we force quoted = false
                // Based on the change above, this will never be true
                if (c == '"' && quoted)
                {
                    if (GetNextChar(false) == '"')
                        // double quotes within quoted string means add a quote       
                        item.Append(GetNextChar(true));
                    else
                        // end-quote reached
                        postdata = true;
                    continue;
                }

                // all cases covered, character must be data
                item.Append(c);
            }
        }

        private char[] buffer = new char[4096];
        private int pos = 0;
        private int length = 0;

        private char GetNextChar(bool eat)
        {
            if (pos >= length)
            {
                length = stream.ReadBlock(buffer, 0, buffer.Length);
                if (length == 0)
                {
                    EOS = true;
                    return '\0';
                }
                pos = 0;
            }
            if (eat)
                return buffer[pos++];
            else
                return buffer[pos];
        }
    }

    public class CsvStreamComma
    {
        private TextReader stream;

        public CsvStreamComma(TextReader s)
        {
            stream = s;
        }

        public string[] GetNextRow()
        {
            ArrayList row = new ArrayList();
            while (true)
            {
                string item = GetNextItem();
                if (item == null)
                    return row.Count == 0 ? null : (string[])row.ToArray(typeof(string));
                row.Add(item);
            }
        }

        private bool EOS = false;
        private bool EOL = false;

        private string GetNextItem()
        {
            if (EOL)
            {
                // previous item was last in line, start new line
                EOL = false;
                return null;
            }

            bool quoted = false;
            bool predata = true;
            bool postdata = false;
            StringBuilder item = new StringBuilder();

            while (true)
            {
                char c = GetNextChar(true);
                if (EOS)
                    return item.Length > 0 ? item.ToString() : null;

                // 20170715 Update this to also deal with tabs
                if ((postdata || !quoted) && c == ',')
                    // end of item, return
                    return item.ToString();

                if ((predata || postdata || !quoted) && (c == '\x0A' || c == '\x0D'))
                {
                    // we are at the end of the line, eat newline characters and exit
                    EOL = true;
                    if (c == '\x0D' && GetNextChar(false) == '\x0A')
                        // new line sequence is 0D0A
                        GetNextChar(true);
                    return item.ToString();
                }

                if (predata && c == ' ')
                    // whitespace preceeding data, discard
                    continue;

                if (predata && c == '"')
                {
                    // quoted data is starting
                    quoted = true;
                    predata = false;
                    continue;
                }

                if (predata)
                {
                    // data is starting without quotes
                    predata = false;
                    item.Append(c);
                    continue;
                }

                if (c == '"' && quoted)
                {
                    if (GetNextChar(false) == '"')
                        // double quotes within quoted string means add a quote       
                        item.Append(GetNextChar(true));
                    else
                        // end-quote reached
                        postdata = true;
                    continue;
                }

                // all cases covered, character must be data
                item.Append(c);
            }
        }

        private char[] buffer = new char[4096];
        private int pos = 0;
        private int length = 0;

        private char GetNextChar(bool eat)
        {
            if (pos >= length)
            {
                length = stream.ReadBlock(buffer, 0, buffer.Length);
                if (length == 0)
                {
                    EOS = true;
                    return '\0';
                }
                pos = 0;
            }
            if (eat)
                return buffer[pos++];
            else
                return buffer[pos];
        }
    }


    static class HelperUtilities
    {

        public static IEnumerable<List<T>> splitList<T>(List<T> locations, int nSize = 100)
        {
            for (int i = 0; i < locations.Count; i += nSize)
            {
                yield return locations.GetRange(i, Math.Min(nSize, locations.Count - i));
            }
        }
        public static DataTable convertDataTableColumn2LowerCase(DataTable sourceTable, string[] columnNames)
        {
            for (int i = 0; i < sourceTable.Rows.Count; i++)
            {
                for (int y = 0; y < columnNames.Length; y++)
                {
                    sourceTable.Rows[i][columnNames[y]] = Convert.ToString(sourceTable.Rows[i][columnNames[y]]).ToLower().Replace(".invalid", "");
                }
            }
            return sourceTable;
        }

        // 20170619 - add preprocessing to load file.
        public static DataTable cleanDataTable(DataTable sourceDataTable)
        {
            DataTable referenceTable = new DataTable();
            referenceTable = sourceDataTable.Copy();

            // 20170619 - this is the original cleanse            
            for (int row = sourceDataTable.Rows.Count - 1; row > -1; row--)
            {
                string column0 = Convert.ToString(sourceDataTable.Rows[row][0]);
                if (column0.Length == 0)
                {
                    sourceDataTable.Rows[row].Delete();
                    continue;
                }

                // SUPVID is column 6
                string column6 = Convert.ToString(sourceDataTable.Rows[row][6]);

                char[] column0Array = column0.ToLower().ToArray();
                char[] column6Array = column6.ToLower().ToArray();

                // we still want to trim
                /*for (int col = 0; col < sourceDataTable.Columns.Count; col++)
                {
                    sourceDataTable.Rows[row][col] = Convert.ToString(sourceDataTable.Rows[row][col]).Trim();
                }*/

                if ((column0Array[0] == 't' || column0Array[0] == 'x') &&
                    (column6Array[0] == 't' || column6Array[0] == 'x'))
                {
                    // we can do other processing here
                    for (int col = 0; col < sourceDataTable.Columns.Count; col++)
                    {
                        sourceDataTable.Rows[row][col] = Convert.ToString(sourceDataTable.Rows[row][col]).Trim();
                    }

                }
                else
                    sourceDataTable.Rows[row].Delete();

            }


            // 20170619 - this is the new section
            if (true)  //flag for troubleshooting
                for (int row = sourceDataTable.Rows.Count - 1; row > -1; row--)
                {
                    // we want to check the manager email field and see if it is blank.
                    // this step is inefficent but tat is OK since we only run the program once a day

                    // col9 is C_EMAILADDR - manager's email address
                    string managerEmail = Convert.ToString(sourceDataTable.Rows[row][9]);
                    if (managerEmail.Length == 0)
                    {
                        // we try and match based on numeric portion of ID and first name and last name
                        string managerFirstName = Convert.ToString(sourceDataTable.Rows[row][7]); //SUPVFNAME
                        string managerLastName = Convert.ToString(sourceDataTable.Rows[row][8]); //SUPVLNAME
                        string managerID = Convert.ToString(sourceDataTable.Rows[row][6]); //SUPVID
                        managerID = managerID.ToLower().Replace("t", "").Replace("x", "");

                        // we do a query
                        DataRow[] rows = referenceTable.Select("[AGTFNAME] like'" + Convert.ToString(sourceDataTable.Rows[row][7]).Replace("'", "''") + "' AND [AGTLNAME] like '" + Convert.ToString(sourceDataTable.Rows[row][8]).Replace("'", "''") + "' AND [AGTID] like '%" + managerID + "%'");

                        string rowsEmail = "";
                        bool transfer = true;
                        if (rows.Length > 0)
                        {
                            for (int i = 0; i < rows.Length; i++)
                            {
                                if (rowsEmail.Length > 0)
                                {
                                    if (rowsEmail != Convert.ToString(rows[i]["AGTEMAIL"]))
                                        transfer = false;
                                }
                                else
                                {
                                    rowsEmail = Convert.ToString(rows[i]["AGTEMAIL"]);
                                }
                            }
                        }

                        if (transfer)
                            sourceDataTable.Rows[row][9] = rowsEmail;

                    }

                }



            return sourceDataTable;
        }

        public static DataTable logBlankMgrEmailDataTable(DataTable sourceDataTable)
        {
            for (int row = sourceDataTable.Rows.Count - 1; row > -1; row--)
            {
                if (Convert.ToString(sourceDataTable.Rows[row][9]).Length > 0)
                    sourceDataTable.Rows[row].Delete();
            }
            return sourceDataTable;
        }

        public static bool purgeFilesOlderThan(string directoryPath, int days)
        {
            string[] files = Directory.GetFiles(directoryPath);

            foreach (string file in files)
            {
                FileInfo fi = new FileInfo(file);
                if (fi.LastAccessTime < DateTime.Now.AddDays(-days))
                {
                    fi.Delete();
                }
            }
            return true;

        }

        public static bool purgeFile(string file, string log_directory, string load_directory)
        {
            string source = file;
            string destination = file.Replace(load_directory, log_directory) + Convert.ToString(DateTime.UtcNow.Ticks);

            //Directory.Move(source,destination);
            File.Delete(file);
            return true;

        }

        public static void WriteToFile(string file_name, string in_string, int pre_pad, int post_pad)
        {
            try
            {
                FileInfo logFile;
                string timeStamp = Convert.ToString(DateTime.Now);
                string pre_pad_string = "";
                string post_pad_string = "";
                logFile = new FileInfo(file_name);
                StreamWriter log = logFile.AppendText();
                for (int i = 0; i < pre_pad; i++)
                {
                    pre_pad_string += "\r\n";
                }
                for (int i = 0; i < post_pad; i++)
                {
                    post_pad_string += "\r\n";
                }
                log.WriteLine(pre_pad_string + in_string + post_pad_string);
                log.Close();

            }
            catch
            {

            }
        }

        public static void WriteToOutputFile(string file_name, string in_string, int pre_pad, int post_pad, bool overwrite)
        {
            try
            {
                FileInfo outputFile;
                string pre_pad_string = "";
                string post_pad_string = "";

                if (overwrite)
                    File.Delete(file_name);

                outputFile = new FileInfo(file_name);
                StreamWriter log = outputFile.AppendText();
                for (int i = 0; i < pre_pad; i++)
                {
                    pre_pad_string += "\r\n";
                }
                for (int i = 0; i < post_pad; i++)
                {
                    post_pad_string += "\r\n";
                }
                log.WriteLine(pre_pad_string + in_string + post_pad_string);
                log.Close();

            }
            catch
            {

            }
        }
        //    public static void WriteToFile(string in_string, int in_mode)
        //    {
        //        try
        //        {
        //            int loglevel_from_config = Convert.ToInt32(ConfigurationSettings.AppSettings["logfile_logmode"]);
        //            if (in_mode <= loglevel_from_config)
        //            {
        //                FileInfo logFile;
        //                string timeStamp = Convert.ToString(DateTime.Now);
        //                logFile = new FileInfo(ConfigurationSettings.AppSettings["log_folder"] + "\\" + HelperUtilities.DetermineLogFileName());
        //                StreamWriter log = logFile.AppendText();
        //                log.WriteLine(timeStamp + " >> " + in_string);
        //                log.Close();
        //            }
        //        }
        //        catch
        //        {

        //        }
        //    }

        //    public static void WriteToFile(int blank_line, string in_string, int in_mode)
        //    {
        //        try
        //        {
        //            int loglevel_from_config = Convert.ToInt32(ConfigurationSettings.AppSettings["logfile_logmode"]);
        //            if (in_mode <= loglevel_from_config)
        //            {
        //                FileInfo logFile;
        //                string timeStamp = Convert.ToString(DateTime.Now);
        //                logFile = new FileInfo(ConfigurationSettings.AppSettings["log_folder"] + "\\" + HelperUtilities.DetermineLogFileName());
        //                StreamWriter log = logFile.AppendText();
        //                for (int i = 0; i < blank_line - 1; i++)
        //                { log.WriteLine("\r\n"); }
        //                log.WriteLine(timeStamp + " >> " + in_string);
        //                log.Close();
        //            }
        //        }
        //        catch
        //        {

        //        }
        //    }


        //    public static string DetermineLogFileName()
        //    {
        //        string file_name = Convert.ToString(DateTime.Today.Year) + "-" +
        //                            Convert.ToString(DateTime.Today.Month) + "-" +
        //                                Convert.ToString(DateTime.Today.Day) + "-log.txt";
        //        return file_name;
        //    }

        // Need an SMTP server to connect to
        public static string SendMail(string toList, string from, string ccList, string subject, string body, string uid, string pwd, string mailhost)
        {
            //MailMessage message = new MailMessage();
            //SmtpClient smtpClient = new SmtpClient();
            string msg = string.Empty;
            try
            {
                //MailAddress fromAddress = new MailAddress(from);
                //message.From = fromAddress;
                //message.To.Add(toList);
                //if (ccList != null && ccList != string.Empty)
                //    message.CC.Add(ccList);
                //message.Subject = subject;
                //message.IsBodyHtml = true;
                //message.Body = body;
                //smtpClient.Host = mailhost;
                //smtpClient.Port = 1025;
                //smtpClient.UseDefaultCredentials = true;
                //smtpClient.Credentials = new System.Net.NetworkCredential(uid, pwd);

                //smtpClient.Send(message);
                //msg = "Successful";
            }
            catch (Exception ex)
            {
                msg = ex.Message;
            }
            return msg;
        }

        public static string[] getTransactionFiles(string folder_path)
        {
            DirectoryInfo info = new DirectoryInfo(folder_path);
            FileInfo[] files = info.GetFiles().OrderBy(p => p.LastWriteTimeUtc).ToArray();

            List<string> fileNames = new List<string>();
            foreach (FileInfo file in files)
            {
                if (file.Name.IndexOf("askRPO_") > -1)
                    fileNames.Add(file.FullName);
            }

            //return Directory.GetFiles(folder_path);
            return fileNames.ToArray();

        }

        public static DataTable readCSVFile(string fileName, bool header)
        {
            // Instantiate a new DataTable object
            DataTable tbl = new DataTable();

            // Load information from the CSV file using a stream
            using (TextReader myStream = new StreamReader(fileName))
            {
                // Load information using the CSVParser utility
                tbl = csvParser.Parse(myStream, header);
            }

            // Return the loaded DataTable object to the caller
            return tbl;
        }

        // to make a string from a datatable
        public static string Table2String(DataTable inputTable)
        {
            string tabledString = "";

            if (inputTable.Rows.Count > 0)
            {
                // do the headers
                for (int col = 0; col < inputTable.Columns.Count; col++)
                {
                    tabledString += inputTable.Columns[col].ColumnName;
                    if (col < inputTable.Columns.Count - 1)
                        tabledString += ",";
                }
                tabledString += "\r\n";

                // do the data
                for (int rows = 0; rows < inputTable.Rows.Count; rows++)
                {
                    for (int cols = 0; cols < inputTable.Columns.Count; cols++)
                    {
                        tabledString += Convert.ToString(inputTable.Rows[rows][cols]);
                        if (cols < inputTable.Columns.Count - 1)
                            tabledString += ",";
                    }

                    if (rows < inputTable.Rows.Count - 1)
                        tabledString += "\r\n";
                }
            }
            return tabledString;
        }


        public static long getUnixTime()
        {
            return Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds);
        }
    }
}