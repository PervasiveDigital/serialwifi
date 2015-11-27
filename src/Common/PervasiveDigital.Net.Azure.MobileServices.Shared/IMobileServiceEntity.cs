using System;

namespace PervasiveDigital.Net.Azure.MobileService
{
    /// <summary>
    /// Interface for Mobile Services entity into tables
    /// </summary>
    public interface IMobileServiceEntity
    {
        /// <summary>
        /// Id
        /// </summary>
        int Id { get; set; }

        /// <summary>
        /// Return JSON representation for current instance
        /// </summary>
        /// <returns>JSON representation</returns>
        string ToJson();

        /// <summary>
        /// Fill current instance fields parsing JSON representation
        /// </summary>
        /// <param name="json">JSON representation to parse</param>
        void Parse(string json);
    }
}
