using System;

namespace PervasiveDigital.Net.Azure.MobileService
{
    /// <summary>
    /// Table for Windows Azure Mobile Services
    /// </summary>
    public class MobileServiceTable : IMobileServiceTable
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="tableName">Table name</param>
        /// <param name="client">Mobile Services client</param>
        public MobileServiceTable(string tableName, MobileServiceClient client)
        {
            this.TableName = tableName;
            this.Client = client;
        }

        #region IMobileServiceTable...

        public MobileServiceClient Client { get; private set; }

        public string TableName { get; private set; }       

        public string Insert(IMobileServiceEntity entity, bool noscript = false)
        {
            return this.Client.Insert(this.TableName, entity, noscript);
        }

        public string Update(IMobileServiceEntity entity, bool noscript = false)
        {
            return this.Client.Update(this.TableName, entity, noscript);
        }

        public bool Delete(int entityId, bool noscript = false)
        {
            return this.Client.Delete(this.TableName,entityId, noscript);
        }

        public string Query(string query, bool noscript = false)
        {
            return this.Client.Query(this.TableName, query, noscript);
        }

        #endregion
    }
}
