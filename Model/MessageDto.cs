using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Parser.Model
{
    public class MessageDto
    {
        public Uri service;

        public string operation;

        public Body request;

        public Body response;

        [JsonIgnore]
        public string service_component
        {
            get
            {
                return service?.AbsolutePath;
            }
        }

    }

    public class Body
    {
        public Dictionary<string,string[]> formatted_data;
        public Dictionary<string,string> headers;

        public string raw_data;

    }
}