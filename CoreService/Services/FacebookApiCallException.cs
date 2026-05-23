using CoreService.Models;
using System.Net;

namespace CoreService.Services
{
    public sealed class FacebookApiCallException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public FbError? FacebookError { get; }

        public FacebookApiCallException(HttpStatusCode code, FbError? err, string msg)
            : base(msg)
        {
            StatusCode = code;
            FacebookError = err;
        }
    }
}
