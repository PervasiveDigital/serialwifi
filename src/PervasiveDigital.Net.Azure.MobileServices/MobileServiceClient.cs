using System;
using System.Text;
using PervasiveDigital.Net;

namespace PervasiveDigital.Net.Azure.MobileService
{
    /// <summary>
    /// Client for Windows Azure Mobile Services via REST API
    /// </summary>
    public class MobileServiceClient
    {
        #region Constants...

        // windows azure mobile services REST headers
        private const string X_ZUMO_APPLICATION_HEADER = "X-ZUMO-APPLICATION";
        private const string X_ZUMO_AUTH_HEADER = "X-ZUMO-AUTH";
        private const string X_ZUMO_MASTER_HEADER = "X-ZUMO-MASTER";
        public const string X_ZUMO_VERSION = "x-zumo-version";
        // buffer size for body
        private const int BUFFER_SIZE = 1024;

        #endregion

        #region Fields...

        // application URI
        private Uri applicationUri;
        // application key, master key and authentication token
        private string applicationKey;
        private string masterKey;
        private string authenticationToken;

        // HTTP client for the requests
        private HttpClient httpClient;
        // HTTP request object
        private HttpRequest httpRequest;
        // HTTP body request and response
        private StringBuilder body;
        private byte[] buffer;
        // building uri
        private StringBuilder uri;

        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="adapter">The wifi adapter to use for network communication</param>
        /// <param name="applicationUri">Application URI</param>
        /// <param name="masterKey">Master Key</param>
        /// <param name="applicationKey">Application Key</param>
        /// <param name="authenticationToken">Authentication Token</param>
        public MobileServiceClient(INetworkAdapter adapter, Uri applicationUri, string masterKey = null,
                                                        string applicationKey = null,                                             
                                                        string authenticationToken = null)
        {
            if (applicationUri == null)
                throw new ArgumentNullException("applicationUri parameter cannot be null");

            this.applicationUri = applicationUri;

            this.masterKey = masterKey;
            this.applicationKey = applicationKey;
            this.authenticationToken = authenticationToken;
            
            // create HTTP client and assign send/receive body callbacks
            this.httpClient = new HttpClient(adapter);

            // create HTTP request
            this.httpRequest = new HttpRequest();
            this.PrepareZumoHeaders();
            
            this.body = new StringBuilder();
            this.buffer = new byte[BUFFER_SIZE];

            this.uri = new StringBuilder();
        }

        /// <summary>
        /// Prepare ZUMO headers
        /// </summary>
        private void PrepareZumoHeaders()
        {
            // only authenticated users
            if (this.masterKey != null)
                this.httpRequest.Headers.Add(X_ZUMO_MASTER_HEADER, this.masterKey);
            // access to anybody with the application key
            else if (this.applicationKey != null)
                this.httpRequest.Headers.Add(X_ZUMO_APPLICATION_HEADER, this.applicationKey);
            // only scripts and admins
            else if (this.authenticationToken != null)
                this.httpRequest.Headers.Add(X_ZUMO_AUTH_HEADER, this.authenticationToken);
        }

        /// <summary>
        /// Insert an entity into table
        /// </summary>
        /// <param name="table">Table name</param>
        /// <param name="entity">Entity object</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>JSON string object result</returns>
        internal string Insert(string table, IMobileServiceEntity entity, bool noscript = false)
        {
            // fill body
            this.body.Clear();
            this.body.Append(entity.ToJson());

            // build URI
            this.uri.Clear();
            this.uri.Append(this.applicationUri.AbsoluteUri)
                .Append("tables/")
                .Append(table);

            if (noscript)
            {
                if (this.masterKey == null)
                    throw new ArgumentException("For noscript you must also supply the service master key");
                this.uri.Append("?noscript=true");
            }

            this.httpRequest.Uri = new Uri(this.uri.ToString());
            this.httpRequest.Method = HttpMethod.Post;
            this.httpRequest.Body = Encoding.UTF8.GetBytes(body.ToString());
            this.httpRequest.ContentLength = this.httpRequest.Body.Length;
            
            HttpResponse httpResp = this.httpClient.Send(this.httpRequest);

            return httpResp.GetBodyAsString();
        }

        /// <summary>
        /// Update an entity into table
        /// </summary>
        /// <param name="table">Table name</param>
        /// <param name="entity">Entity object</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>JSON string object result</returns>
        internal string Update(string table, IMobileServiceEntity entity, bool noscript = false)
        {
            // fill body
            this.body.Clear();
            this.body.Append(entity.ToJson());

            // build URI
            this.uri.Clear();
            this.uri.Append(this.applicationUri.AbsoluteUri)
                .Append("tables/")
                .Append(table)
                .Append("/")
                .Append(entity.Id);

            if (noscript)
            {
                if (this.masterKey == null)
                    throw new ArgumentException("For noscript you must also supply the service master key");
                this.uri.Append("?noscript=true");
            }

            this.httpRequest.Uri = new Uri(this.uri.ToString());
            this.httpRequest.Method = HttpMethod.Patch;
            this.httpRequest.Body = Encoding.UTF8.GetBytes(body.ToString());
            this.httpRequest.ContentLength = this.httpRequest.Body.Length;

            HttpResponse httpResp = this.httpClient.Send(this.httpRequest);

            return httpResp.GetBodyAsString();
        }

        /// <summary>
        /// Delete an entity from table
        /// </summary>
        /// <param name="table">Table name</param>
        /// <param name="entityId">Entity Id</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>Operation result</returns>
        internal bool Delete(string table, int entityId, bool noscript = false)
        {
            this.body.Clear();

            // build URI
            this.uri.Clear();
            this.uri.Append(this.applicationUri.AbsoluteUri)
                .Append("tables/")
                .Append(table)
                .Append("/")
                .Append(entityId);

            if (noscript)
            {
                if (this.masterKey == null)
                    throw new ArgumentException("For noscript you must also supply the service master key");
                this.uri.Append("?noscript=true");
            }

            this.httpRequest.Uri = new Uri(this.uri.ToString());
            this.httpRequest.ContentLength = 0;
            this.httpRequest.Method = HttpMethod.Delete;

            HttpResponse httpResp = this.httpClient.Send(this.httpRequest);

            return (httpResp.StatusCode == HttpStatusCode.NoContent);
        }

        /// <summary>
        /// Query on table
        /// </summary>
        /// <param name="table">TableName</param>
        /// <param name="query">Query string</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>JSON string object result</returns>
        internal string Query(string table, string query, bool noscript = false)
        {
            this.body.Clear();

            // build URI
            this.uri.Clear();
            this.uri.Append(this.applicationUri.AbsoluteUri)
                .Append("tables/")
                .Append(table);

            if ((query != null) && (query.Length > 0))
            {
                this.uri.Append("?")
                    .Append(query);
            }

            if (noscript)
            {
                if (this.masterKey == null)
                    throw new ArgumentException("For noscript you must also supply the service master key");
                if ((query != null) && (query.Length > 0))
                    this.uri.Append("&");
                else
                    this.uri.Append("?");

                this.uri.Append("noscript=true");
            }

            this.httpRequest.Uri = new Uri(this.uri.ToString());
            this.httpRequest.ContentLength = 0;
            this.httpRequest.Method = HttpMethod.Get;

            HttpResponse httpResp = this.httpClient.Send(this.httpRequest);

            return httpResp.GetBodyAsString();
        }

        /// <summary>
        /// Get table reference
        /// </summary>
        /// <param name="tableName">Table name</param>
        /// <returns>Table reference</returns>
        public IMobileServiceTable GetTable(string tableName)
        {
            return new MobileServiceTable(tableName, this);
        }
    }
}
