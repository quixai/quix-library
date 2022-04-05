// namespace Quix.Snowflake.Writer.Configuration
// {
//     /// <summary>
//     /// Broker connection details of the Workspace
//     /// </summary>
//     public class WorkspaceBrokerDetails
//     {
//         /// <summary>
//         /// Kafka Broker Address
//         /// </summary>
//         public string BrokerAddress { get; set; }
//
//         /// <summary>
//         /// Kafka security mode.
//         /// </summary>
//         public BrokerSecurityMode SecurityMode { get; set; }
//
//         /// <summary>
//         /// Client certificates to establish the ssl connection to Kafka
//         /// </summary>
//         public string SslCertificatesUrl { get; set; }
//
//         /// <summary>
//         /// SSL password.
//         /// </summary>
//         public string SslPassword { get; set; }
//
//         /// <summary>
//         /// SASL mechanism (PLAIN, SCRAM-SHA-256, etc)
//         /// </summary>
//         public BrokerSaslMechanism SaslMechanism { get; set; }
//
//         /// <summary>
//         /// SASL username.
//         /// </summary>
//         public string Username { get; set; }
//
//         /// <summary>
//         /// SASL password. 
//         /// </summary>
//         public string Password { get; set; }
//     }
//
//     /// <summary>
//     /// Broker security mode.
//     /// </summary>
//     public enum BrokerSecurityMode
//     {
//         /// <summary>
//         /// Plain Text mode
//         /// </summary>
//         PlainText,
//
//         /// <summary>
//         /// SSL authorized without ACL.
//         /// </summary>
//         Ssl,
//
//         /// <summary>
//         /// SSL secured ACL role system.
//         /// </summary>
//         SaslSsl,
//     }
//
//     /// <summary>
//     /// SaslMechanism enum values
//     /// </summary>
//     public enum BrokerSaslMechanism
//     {
//         /// <summary>
//         /// GSSAPI
//         /// </summary>
//         Gssapi,
//
//         /// <summary>
//         /// PLAIN
//         /// </summary>
//         Plain,
//
//         /// <summary>
//         /// SCRAM-SHA-256
//         /// </summary>
//         ScramSha256,
//
//         /// <summary>
//         /// SCRAM-SHA-512
//         /// </summary>
//         ScramSha512,
//
//         /// <summary>
//         /// OAUTHBEARER
//         /// </summary>
//         OAuthBearer
//     }
// }
