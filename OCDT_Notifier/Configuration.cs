using System;
namespace OCDT_Notifier {
    public class Configuration {
        public string Random { get; set; }
        public ServerType Server { get; set; }
        public FetchType Fetch { get; set; }
        public TargetType Target { get; set; }

        public class ServerType {
            public string Url { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class FetchType {
            public string Models { get; set; }
            public float Interval { get; set; }
        }

        public class TargetType {
            public string Hook { get; set; }
        }

        public Configuration ()
        {
        }
    }
}
