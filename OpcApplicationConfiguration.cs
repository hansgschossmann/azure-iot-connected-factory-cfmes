using Opc.Ua;
using Serilog;

namespace CfMes
{
    using System.Threading.Tasks;

    public partial class OpcApplicationConfiguration
    {
        /// <summary>
        /// Configuration info for the OPC application.
        /// </summary>
        public static ApplicationConfiguration ApplicationConfiguration { get; private set; }
        public static string Hostname
        {
            get => _hostname;
#pragma warning disable CA1308 // Normalize strings to uppercase
            set => _hostname = value.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase
        }

        public static string ApplicationName { get; set; } = "cfmes";

        public static string ApplicationUri => $"urn:{Hostname}:{ApplicationName}:microsoft:";

        public static string ProductUri => "https://github.com/hansgschossmann/azure-iot-connected-factory-cfmes.git";

        /// <summary>
        /// <summary>
        /// Mapping of the application logging levels to OPC stack logging levels.
        /// </summary>
        public static int OpcTraceToLoggerVerbose { get; set; } = 0;
        public static int OpcTraceToLoggerDebug { get; set; } = 0;
        public static int OpcTraceToLoggerInformation { get; set; } = 0;
        public static int OpcTraceToLoggerWarning { get; set; } = 0;
        public static int OpcTraceToLoggerError { get; set; } = 0;
        public static int OpcTraceToLoggerFatal { get; set; } = 0;

        /// <summary>
        /// Set the OPC stack log level.
        /// </summary>
        public static int OpcStackTraceMask { get; set; } = Utils.TraceMasks.Error | Utils.TraceMasks.Security | Utils.TraceMasks.StackTrace | Utils.TraceMasks.StartStop;

        /// <summary>
        /// Configures all OPC stack settings
        /// </summary>
        public async Task<ApplicationConfiguration> ConfigureAsync(ILogger logger)
        {
            _logger = logger;
            // Instead of using a Config.xml we configure everything programmatically.

            //
            // OPC UA Application configuration
            //
            ApplicationConfiguration = new ApplicationConfiguration
            {
                // basic settings
                ApplicationName = ApplicationName,
                ApplicationUri = ApplicationUri,
                ProductUri = ProductUri,
                ApplicationType = ApplicationType.Client,

                //
                // TraceConfiguration
                //
                TraceConfiguration = new TraceConfiguration()
            };
            ApplicationConfiguration.TraceConfiguration.TraceMasks = OpcStackTraceMask;
            ApplicationConfiguration.TraceConfiguration.ApplySettings();
            Utils.Tracing.TraceEventHandler += LoggerOpcUaTraceHandler;
            _logger.Information($"opcstacktracemask set to: 0x{OpcStackTraceMask:X}");

            //
            // Security configuration
            //
            // security configuration
            await InitApplicationSecurityAsync().ConfigureAwait(false);

            //
            // TransportConfigurations
            //
            ApplicationConfiguration.TransportQuotas = new TransportQuotas();

            // add default client configuration
            ApplicationConfiguration.ClientConfiguration = new ClientConfiguration();

            // validate the configuration now
            await ApplicationConfiguration.Validate(ApplicationConfiguration.ApplicationType).ConfigureAwait(false);
            return ApplicationConfiguration;
        }

        /// <summary>
        /// Event handler to log OPC UA stack trace messages into own logger.
        /// </summary>
        private static void LoggerOpcUaTraceHandler(object sender, TraceEventArgs e)
        {
            // return fast if no trace needed
            if ((e.TraceMask & OpcStackTraceMask) == 0)
            {
                return;
            }
            // e.Exception and e.Message are always null

            // format the trace message
            string message = string.Format(e.Format, e.Arguments).Trim();
            message = "OPC: " + message;

            // map logging level
            if ((e.TraceMask & OpcTraceToLoggerVerbose) != 0)
            {
                _logger.Verbose(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerDebug) != 0)
            {
                _logger.Debug(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerInformation) != 0)
            {
                _logger.Information(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerWarning) != 0)
            {
                _logger.Warning(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerError) != 0)
            {
                _logger.Error(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerFatal) != 0)
            {
                _logger.Fatal(message);
                return;
            }
            return;
        }

#pragma warning disable CA1308 // Normalize strings to uppercase
        private static string _hostname = $"{Utils.GetHostName().ToLowerInvariant()}";
#pragma warning restore CA1308 // Normalize strings to uppercase

        private static ILogger _logger;
    }
}
