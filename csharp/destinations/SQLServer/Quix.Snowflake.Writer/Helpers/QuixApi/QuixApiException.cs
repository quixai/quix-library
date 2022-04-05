using System;
using System.Net;

namespace Quix.Snowflake.Writer.Helpers.QuixApi
{
    /// <summary>
    /// API exception converted to a c# exception
    /// </summary>
    public class QuixApiException : Exception
    {
        public QuixApiException(string endpoint, string msg, string cid, HttpStatusCode httpStatusCode) : base(ConvertToMessage(endpoint, msg, cid, httpStatusCode))
        {
        }

        private static string ConvertToMessage(string endpoint, string msg, string cid, HttpStatusCode httpStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(cid))
            {
                return $"Request failed ({(int)httpStatusCode}) to {endpoint} with message: {msg}{(msg.EndsWith(".") ? "" : ".")} If you need help, contact us with Correlation Id {cid}.";
            }

            return $"Request failed ({(int)httpStatusCode}) to {endpoint} with message: {msg}.";
        }
    }
}