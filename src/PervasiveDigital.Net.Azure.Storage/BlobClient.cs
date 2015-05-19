using System;
using System.Collections;
using System.IO;
using System.Text;
using Microsoft.SPOT;

using PervasiveDigital.Net;
using PervasiveDigital.Utilities;

namespace PervasiveDigital.Net.Azure.Storage
{
    public class BlobClient
    {
        private readonly CloudStorageAccount _account;

        public BlobClient(CloudStorageAccount account)
        {
            _account = account;
            DateHeader = DateTime.Now.ToString("R");
        }

        public bool PutBlockBlob(string containerName, string blobName, string fileNamePath)
        {
            try
            {
                string deploymentPath =
                    StringUtilities.Format("{0}/{1}/{2}", _account.UriEndpoints["Blob"], containerName,
                                         blobName);
                int contentLength;

                HttpVerb = "PUT";

                byte[] ms = GetPackageFileBytesAndLength(fileNamePath, out contentLength);

                string canResource = StringUtilities.Format("/{0}/{1}/{2}", _account.AccountName, containerName, blobName);

                string authHeader = CreateAuthorizationHeader(canResource, "\nx-ms-blob-type:BlockBlob", contentLength);

                try
                {
                    var blobTypeHeaders = new Hashtable();
                    blobTypeHeaders.Add("x-ms-blob-type", "BlockBlob");
                    var response = AzureStorageHttpHelper.SendWebRequest(deploymentPath, authHeader, DateHeader, VersionHeader, ms, contentLength, HttpVerb, true, blobTypeHeaders);
                    if (response.StatusCode != HttpStatusCode.Created)
                    {
                        Debug.Print("Deployment Path was " + deploymentPath);
                        Debug.Print("Auth Header was " + authHeader);
                        Debug.Print("Ms was " + ms.Length);
                        Debug.Print("Length was " + contentLength);
                    }
                    else
                    {
                        Debug.Print("Success");
                        Debug.Print("Auth Header was " + authHeader);
                    }

                    return response.StatusCode == HttpStatusCode.Created;
                }
                catch (WebException wex)
                {
                    Debug.Print(wex.ToString());
                    return false;
                }
            }
            catch (IOException ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }

            return true;
        }

        public bool PutBlockBlob(string containerName, string blobName, Byte[] ms)
        {
            try
            {
                string deploymentPath =
                    StringUtilities.Format("{0}/{1}/{2}", _account.UriEndpoints["Blob"], containerName,
                                         blobName);
                int contentLength = ms.Length;

                HttpVerb = "PUT";

                string canResource = StringUtilities.Format("/{0}/{1}/{2}", _account.AccountName, containerName, blobName);

                string authHeader = CreateAuthorizationHeader(canResource, "\nx-ms-blob-type:BlockBlob", contentLength);

                try
                {
                    var blobTypeHeaders = new Hashtable();
                    blobTypeHeaders.Add("x-ms-blob-type", "BlockBlob");
                    var response = AzureStorageHttpHelper.SendWebRequest(deploymentPath, authHeader, DateHeader, VersionHeader, ms, contentLength, HttpVerb, true, blobTypeHeaders);
                    if (response.StatusCode != HttpStatusCode.Created)
                    {
                        Debug.Print("Deployment Path was " + deploymentPath);
                        Debug.Print("Auth Header was " + authHeader);
                        Debug.Print("Ms was " + ms.Length);
                        Debug.Print("Length was " + contentLength);
                    }
                    else
                    {
                        Debug.Print("Success");
                        Debug.Print("Auth Header was " + authHeader);
                    }

                    return response.StatusCode == HttpStatusCode.Created;
                }
                catch (WebException wex)
                {
                    Debug.Print(wex.ToString());
                    return false;
                }
            }
            catch (IOException ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }

            return true;
        }

        public bool DeleteBlob(string containerName, string blobName)
        {
            try
            {
                string deploymentPath =
                    StringUtilities.Format("{0}/{1}/{2}", _account.UriEndpoints["Blob"], containerName,
                                         blobName);

                HttpVerb = "DELETE";

                string canResource = StringUtilities.Format("/{0}/{1}/{2}", _account.AccountName, containerName, blobName);

                string authHeader = CreateAuthorizationHeader(canResource);

                try
                {
                    var response = AzureStorageHttpHelper.SendWebRequest(deploymentPath, authHeader, DateHeader, VersionHeader, null, 0, HttpVerb, true);
                    if (response.StatusCode != HttpStatusCode.Accepted)
                    {
                        Debug.Print("Deployment Path was " + deploymentPath);
                        Debug.Print("Auth Header was " + authHeader);
                        Debug.Print("Error Status Code: " + response.StatusCode);
                    }
                    else
                    {
                        Debug.Print("Success");
                        Debug.Print("Auth Header was " + authHeader);
                    }

                    return response.StatusCode == HttpStatusCode.Accepted;
                }
                catch (WebException wex)
                {
                    Debug.Print(wex.ToString());
                    return false;
                }
            }
            catch (IOException ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
        }

        public bool CreateContainer(string containerName)
        {
            try
            {
                string deploymentPath =
                    StringUtilities.Format("{0}/{1}?{2}", _account.UriEndpoints["Blob"], containerName, ContainerString);

                HttpVerb = "PUT";

                string canResource = StringUtilities.Format("/{0}/{1}\nrestype:container", _account.AccountName,
                    containerName);

                string authHeader = CreateAuthorizationHeader(canResource);

                try
                {
                    var response = AzureStorageHttpHelper.SendWebRequest(deploymentPath, authHeader, DateHeader, VersionHeader, null, 0, HttpVerb, true);
                    if (response.StatusCode != HttpStatusCode.Created)
                    {
                        Debug.Print("Deployment Path was " + deploymentPath);
                        Debug.Print("Auth Header was " + authHeader);
                        Debug.Print("Error Status Code: " + response.StatusCode);
                    }
                    else
                    {
                        Debug.Print("Success");
                        Debug.Print("Auth Header was " + authHeader);
                    }

                    return response.StatusCode == HttpStatusCode.Created;
                }
                catch (WebException wex)
                {
                    Debug.Print(wex.ToString());
                    return false;
                }
            }
            catch (IOException ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
        }

        public bool DeleteContainer(string containerName)
        {
            try
            {
                string deploymentPath =
                    StringUtilities.Format("{0}/{1}?{2}", _account.UriEndpoints["Blob"], containerName, ContainerString);

                HttpVerb = "DELETE";

                string canResource = StringUtilities.Format("/{0}/{1}\nrestype:container", _account.AccountName,
                    containerName);

                string authHeader = CreateAuthorizationHeader(canResource);

                try
                {
                    var response = AzureStorageHttpHelper.SendWebRequest(deploymentPath, authHeader, DateHeader, VersionHeader, null, 0, HttpVerb, true);
                    if (response.StatusCode != HttpStatusCode.Accepted)
                    {
                        Debug.Print("Deployment Path was " + deploymentPath);
                        Debug.Print("Auth Header was " + authHeader);
                        Debug.Print("Error Status Code: " + response.StatusCode);
                    }
                    else
                    {
                        Debug.Print("Success");
                        Debug.Print("Auth Header was " + authHeader);
                    }

                    return response.StatusCode == HttpStatusCode.Accepted;
                }
                catch (WebException wex)
                {
                    Debug.Print(wex.ToString());
                    return false;
                }
            }
            catch (IOException ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
            catch (Exception ex)
            {
                Debug.Print(ex.ToString());
                return false;
            }
        }

        protected byte[] GetPackageFileBytesAndLength(string fileName, out int contentLength)
        {
            byte[] ms = null;
            contentLength = 0;
            if (fileName != null)
            {
                using (StreamReader sr = new StreamReader(File.Open(fileName, FileMode.Open)))
                {
                    string data = sr.ReadToEnd();
                    ms = Encoding.UTF8.GetBytes(data);
                    contentLength = ms.Length;
                }
            }
            return ms;
        }

        protected string CreateAuthorizationHeader(string canResource, string options = "", int contentLength = 0)
        {
            string toSign = StringUtilities.Format("{0}\n\n\n{1}\n\n\n\n\n\n\n\n{5}\nx-ms-date:{2}\nx-ms-version:{3}\n{4}",
                                          HttpVerb, contentLength, DateHeader, VersionHeader, canResource, options);

            var hmacBytes = SHA.computeHMAC_SHA256(Convert.FromBase64String(_account.AccountKey), Encoding.UTF8.GetBytes(toSign));
            string signature = Convert.ToBase64String(hmacBytes).Replace("!", "+").Replace("*", "/"); ;

            return "SharedKey " + _account.AccountName + ":" + signature;
        }


        internal const string VersionHeader = "2011-08-18";

        private const string ContainerString = "restype=container";

        protected string DateHeader { get; set; }

        public string HttpVerb { get; set; }
    }
}