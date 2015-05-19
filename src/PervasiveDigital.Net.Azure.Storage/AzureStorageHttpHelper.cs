using System;
using System.Collections;
using System.IO;
using Microsoft.SPOT;

namespace PervasiveDigital.Net.Azure.Storage
{
    /// <summary>
    /// A common helper class for HTTP access to Windows Azure Storage
    /// </summary>
    public static class AzureStorageHttpHelper
    {
        /// <summary>
        /// Sends a Web Request prepared for Azure Storage
        /// </summary>
        /// <param name="url"></param>
        /// <param name="authHeader"></param>
        /// <param name="dateHeader"></param>
        /// <param name="versionHeader"></param>
        /// <param name="fileBytes"></param>
        /// <param name="contentLength"></param>
        /// <param name="httpVerb"></param>
        /// <param name="expect100Continue"></param>
        /// <param name="additionalHeaders"></param>
        /// <returns></returns>
        public static BasicHttpResponse SendWebRequest(string url, string authHeader, string dateHeader, string versionHeader, byte[] fileBytes = null, int contentLength = 0, string httpVerb = "GET", bool expect100Continue = false, Hashtable additionalHeaders = null)
        {
            string responseBody = "";
            HttpStatusCode responseStatusCode = HttpStatusCode.Ambiguous;
            HttpRequest request = PrepareRequest(url, authHeader, dateHeader, versionHeader, fileBytes, contentLength, httpVerb, expect100Continue, additionalHeaders);
            try
            {
                HttpResponse response;
                using (response = (HttpResponse)request.GetResponse())
                {
                    responseStatusCode = response.StatusCode;
                    Debug.Print("HTTP " + ((int)responseStatusCode).ToString());

                    using (var responseStream = response.GetResponseStream())
                    using (var reader = new StreamReader(responseStream))
                    {
                        responseBody = reader.ReadToEnd();
                    }

                    if (response.StatusCode == HttpStatusCode.Created)
                    {
                        Debug.Print("Asset has been created!");
                    }
                    if (response.StatusCode == HttpStatusCode.Accepted)
                    {
                        Debug.Print("Action has been completed");
                    }
                    if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        throw new WebException("Forbidden", null, WebExceptionStatus.TrustFailure, response);
                    }                    
                }
            }
            catch (WebException ex)
            {
                if ((ex.Response).StatusCode == HttpStatusCode.Conflict)
                {
                    Debug.Print("Asset already exists!");
                }
                if ((ex.Response).StatusCode == HttpStatusCode.Forbidden)
                {
                    Debug.Print("Problem with signature. Check next debug statement for stack");
                }
            }

            if (responseBody == null)
                responseBody = "There was no body content";

            Debug.Print(responseBody);
            return new BasicHttpResponse() {Body = responseBody, StatusCode = responseStatusCode};
        }

        /// <summary>
        /// Prepares a HttpWebRequest with required headers of x-ms-date, x-ms-version and Authorization
        /// </summary>
        /// <param name="url"></param>
        /// <param name="authHeader"></param>
        /// <param name="dateHeader"></param>
        /// <param name="versionHeader"></param>
        /// <param name="fileBytes"></param>
        /// <param name="contentLength"></param>
        /// <param name="httpVerb"></param>
        /// <returns></returns>
        private static HttpRequest PrepareRequest(string url, string authHeader, string dateHeader, string versionHeader, byte[] fileBytes , int contentLength, string httpVerb, bool expect100Continue = false, Hashtable additionalHeaders = null)
        {
            var uri = new Uri(url);
            var request = (HttpRequest)WebRequest.Create(uri);
            request.Method = httpVerb;
            request.ContentLength = contentLength;
            request.Headers.Add("x-ms-date", dateHeader);
            request.Headers.Add("x-ms-version", versionHeader);
            request.Headers.Add("Authorization", authHeader);

            if (expect100Continue)
            {
                request.Expect = "100-continue";
            }

            if (additionalHeaders != null)
            {
                foreach (var additionalHeader in additionalHeaders.Keys)
                {
                    request.Headers.Add(additionalHeader.ToString(), additionalHeaders[additionalHeader].ToString());
                }
            }

            if (AttachFiddler)
            {
                request.Proxy = new WebProxy("127.0.0.1", 8888);
            }

            if (contentLength != 0)
            {
                request.GetRequestStream().Write(fileBytes, 0, fileBytes.Length);
            }
            return request;
        }

        public static bool AttachFiddler { get; set; }
    }

}