using System;
using System.Collections;

using PervasiveDigital.Utilities;

namespace PervasiveDigital.Net.Azure.Storage
{
    public class CloudStorageAccount
    {
        public string AccountName { get; set; }
        public string AccountKey { get; set; }
        public Hashtable UriEndpoints { get; set; }

        public CloudStorageAccount(string accountName, string accountKey, Hashtable uriEndpoints)
        {
            AccountName = accountName;
            AccountKey = accountKey;
            UriEndpoints = uriEndpoints;

            //checkUriEndpoints
            //must be 3 known keys (Blob, Queue, Table) 
            //must not end with a trailing slash
        }

        public CloudStorageAccount(string accountName, string accountKey) : this (accountName,accountKey,GetDefaultUriEndpoints(accountName))
        {
        }

        private static Hashtable GetDefaultUriEndpoints(string accountName)
        {
            var defaults = new Hashtable(3);
            defaults.Add("Blob", StringUtilities.Format("http://{0}.blob.core.windows.net", accountName));
            defaults.Add("Queue", StringUtilities.Format("http://{0}.queue.core.windows.net", accountName));
            defaults.Add("Table", StringUtilities.Format("http://{0}.table.core.windows.net", accountName));
            return defaults;
        }

        public static CloudStorageAccount Parse(string connectionString)
        {
            throw new NotImplementedException();
        }
    }
}