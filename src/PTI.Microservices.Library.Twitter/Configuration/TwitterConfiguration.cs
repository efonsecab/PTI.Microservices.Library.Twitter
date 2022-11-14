using System;
using System.Collections.Generic;
using System.Text;

namespace PTI.Microservices.Library.Configuration
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public class TwitterConfiguration
    {
        public string AccessToken { get; set; }
        public string AccessTokenSecret { get; set; }
        public string ConsumerKey { get; set; }
        public string ConsumerSecret { get; set; }
        public string ScreenName { get; set; }
        public bool RetryOperationOnFailure { get; set; } = true;
        public int MaxRetryCount { get; set; } = 3;
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
