using System;

namespace PervasiveDigital.Net.Azure.MobileService
{
    /// <summary>
    /// Interface for Mobile Services table
    /// </summary>
    public interface IMobileServiceTable
    {
        /// <summary>
        /// Client for Mobile Services
        /// </summary>
        MobileServiceClient Client { get; }

        /// <summary>
        /// Table name
        /// </summary>
        string TableName { get; }

        /// <summary>
        /// Insert an entity into table
        /// </summary>
        /// <param name="entity">Entity object</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>JSON string object result</returns>
        string Insert(IMobileServiceEntity entity, bool noscript = false);

        /// <summary>
        /// Update an entity into table
        /// </summary>
        /// <param name="entity">Entity object</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>JSON string object result</returns>
        string Update(IMobileServiceEntity entity, bool noscript = false);

        /// <summary>
        /// Delete an entity from table
        /// </summary>
        /// <param name="entityId">Entity Id</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>Operation result</returns>
        bool Delete(int entityId, bool noscript = false);

        /// <summary>
        /// Query on table
        /// </summary>
        /// <param name="query">Query string</param>
        /// <param name="noscript">NoScript flag</param>
        /// <returns>JSON string object result</returns>
        string Query(string query, bool noscript = false);
    }
}
