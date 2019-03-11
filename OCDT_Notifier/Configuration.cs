using System;
namespace OCDT_Notifier {
    public class Configuration {
        /// <summary>
        /// Information about the OCDT server
        /// </summary>
        public ServerType Server { get; set; }

        /// <summary>
        /// Configuration for OCDT data server
        /// </summary>
        /// <value>The fetch.</value>
        public FetchType Fetch { get; set; }

        /// <summary>
        /// Configuration for the messaging target, i.e. the chat application
        /// </summary>
        /// <value>The target.</value>
        public TargetType Target { get; set; }

        /// <summary>
        /// Configuration of the types of messages sent to the target, and of
        /// the way they are sent.
        /// </summary>
        /// <value>The filter.</value>
        public OutputType Output { get; set; }

        /// <summary>
        /// Whether OCDT notifier should be set to debugging mode, that enables some developer-friendly
        /// but not production-friendly configurations.
        /// </summary>
        /// <value><c>true</c> if debug; otherwise, <c>false</c>. Defaults to <c>false</c>.</value>
        public bool Debug { get; set; }

        public class ServerType {
            public string Url { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class FetchType {
            public string Models { get; set; }
            public float Interval { get; set; }
        }

        public class OutputType {
            public bool SplitOwners { get; set; }
            public bool ClearClutter { get; set; }
        }

        public class TargetType {
            public string Hook { get; set; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:OCDT_Notifier.Configuration"/> class, setting default values.
        /// </summary>
        public Configuration ()
        {
            Debug = false;
        }
    }
}
