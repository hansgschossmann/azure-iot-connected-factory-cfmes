
using Opc.Ua;
using System;
using System.Security.Cryptography.X509Certificates;

namespace CfMes
{
    using System.Threading.Tasks;
    using static Opc.Ua.CertificateStoreType;
    using static Program;

    public class OpcApplicationConfiguration
    {
        public static string StationHostnameLabel => (_stationHostname.Contains(".") ? _stationHostname.Substring(0, _stationHostname.IndexOf('.')).ToLowerInvariant() : _stationHostname);

        public static string ApplicationName => $"{StationHostnameLabel}";

        public static string ApplicationUri => $"urn:{StationHostnameLabel}:ConnectedfactoryMes";

        public static string ProductUri => $"https://github.com/azure/azure-iot-connected-factory-cfmes.git";

        public static bool TrustMyself
        {
            get => _trustMyself;
            set => _trustMyself = value;
        }

        // Enable Utils.TraceMasks.OperationDetail to get output for IoTHub telemetry operations. Current: 0x287 (647), with OperationDetail: 0x2C7 (711)
        public static int OpcStackTraceMask
        {
            get => _opcStackTraceMask;
            set => _opcStackTraceMask = value;
        }

        public static string OpcOwnCertStoreType
        {
            get => _opcOwnCertStoreType;
            set => _opcOwnCertStoreType = value;
        }

        public static string OpcOwnCertDirectoryStorePathDefault => "CertificateStores/own";
        public static string OpcOwnCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";
        public static string OpcOwnCertStorePath
        {
            get => _opcOwnCertStorePath;
            set => _opcOwnCertStorePath = value;
        }

        public static string OpcTrustedCertStoreType
        {
            get => _opcTrustedCertStoreType;
            set => _opcTrustedCertStoreType = value;
        }

        public static string OpcTrustedCertDirectoryStorePathDefault => "CertificateStores/trusted";
        public static string OpcTrustedCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";
        public static string OpcTrustedCertStorePath
        {
            get => _opcTrustedCertStorePath;
            set => _opcTrustedCertStorePath = value;
        }

        public static string OpcRejectedCertStoreType
        {
            get => _opcRejectedCertStoreType;
            set => _opcRejectedCertStoreType = value;
        }

        public static string OpcRejectedCertDirectoryStorePathDefault => "CertificateStores/rejected";
        public static string OpcRejectedCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";
        public static string OpcRejectedCertStorePath
        {
            get => _opcRejectedCertStorePath;
            set => _opcRejectedCertStorePath = value;
        }

        public static string OpcIssuerCertStoreType
        {
            get => _opcIssuerCertStoreType;
            set => _opcIssuerCertStoreType = value;
        }

        public static string OpcIssuerCertDirectoryStorePathDefault => "CertificateStores/issuers";
        public static string OpcIssuerCertX509StorePathDefault => "CurrentUser\\UA_MachineDefault";
        public static string OpcIssuerCertStorePath
        {
            get => _opcIssuerCertStorePath;
            set => _opcIssuerCertStorePath = value;
        }

        public static bool AutoAcceptCerts
        {
            get => _autoAcceptCerts;
            set => _autoAcceptCerts = value;
        }

        public static int OpcTraceToLoggerVerbose = 0;
        public static int OpcTraceToLoggerDebug = 0;
        public static int OpcTraceToLoggerInformation = 0;
        public static int OpcTraceToLoggerWarning = 0;
        public static int OpcTraceToLoggerError = 0;
        public static int OpcTraceToLoggerFatal = 0;

        /// <summary>
        /// Configures all OPC stack settings
        /// </summary>
        public async Task<ApplicationConfiguration> ConfigureAsync()
        {
            // Instead of using a Config.xml we configure everything programmatically.

            //
            // OPC UA Application configuration
            //
            _configuration = new ApplicationConfiguration();

            // basic settings
            _configuration.ApplicationName = ApplicationName;
            _configuration.ApplicationUri = ApplicationUri;
            _configuration.ProductUri = ProductUri;
            _configuration.ApplicationType = ApplicationType.Client;

            //
            // TraceConfiguration
            //
            _configuration.TraceConfiguration = new TraceConfiguration();
            _configuration.TraceConfiguration.TraceMasks = _opcStackTraceMask;
            _configuration.TraceConfiguration.ApplySettings();
            Utils.Tracing.TraceEventHandler += new EventHandler<TraceEventArgs>(LoggerOpcUaTraceHandler);
            Logger.Information($"opcstacktracemask set to: 0x{_opcStackTraceMask:X}");

            //
            // Security configuration
            //
            _configuration.SecurityConfiguration = new SecurityConfiguration();

            // TrustedIssuerCertificates
            _configuration.SecurityConfiguration.TrustedIssuerCertificates = new CertificateTrustList();
            _configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType = _opcIssuerCertStoreType;
            _configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath = _opcIssuerCertStorePath;
            Logger.Information($"Trusted Issuer store type is: {_configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType}");
            Logger.Information($"Trusted Issuer Certificate store path is: {_configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath}");

            // TrustedPeerCertificates
            _configuration.SecurityConfiguration.TrustedPeerCertificates = new CertificateTrustList();
            _configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType = _opcTrustedCertStoreType;
            if (string.IsNullOrEmpty(_opcTrustedCertStorePath))
            {
                // Set default.
                _configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath = _opcTrustedCertStoreType == X509Store ? OpcTrustedCertX509StorePathDefault : OpcTrustedCertDirectoryStorePathDefault;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_TPC_SP")))
                {
                    // Use environment variable.
                    _configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath = Environment.GetEnvironmentVariable("_TPC_SP");
                }
            }
            else
            {
                _configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath = _opcTrustedCertStorePath;
            }
            Logger.Information($"Trusted Peer Certificate store type is: {_configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType}");
            Logger.Information($"Trusted Peer Certificate store path is: {_configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath}");

            // RejectedCertificateStore
            _configuration.SecurityConfiguration.RejectedCertificateStore = new CertificateTrustList();
            _configuration.SecurityConfiguration.RejectedCertificateStore.StoreType = _opcRejectedCertStoreType;
            _configuration.SecurityConfiguration.RejectedCertificateStore.StorePath = _opcRejectedCertStorePath;

            Logger.Information($"Rejected certificate store type is: {_configuration.SecurityConfiguration.RejectedCertificateStore.StoreType}");
            Logger.Information($"Rejected Certificate store path is: {_configuration.SecurityConfiguration.RejectedCertificateStore.StorePath}");

            // AutoAcceptUntrustedCertificates
            // This is a security risk and should be set to true only for debugging purposes.
            _configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;

            // AddAppCertToTrustStore: this does only work on Application objects, here for completeness
            _configuration.SecurityConfiguration.AddAppCertToTrustedStore = TrustMyself;

            // RejectSHA1SignedCertificates
            // We allow SHA1 certificates for now as many OPC Servers still use them
            _configuration.SecurityConfiguration.RejectSHA1SignedCertificates = false;
            Logger.Information($"Rejection of SHA1 signed certificates is {(_configuration.SecurityConfiguration.RejectSHA1SignedCertificates ? "enabled" : "disabled")}");

            // MinimunCertificatesKeySize
            // We allow a minimum key size of 1024 bit, as many OPC UA servers still use them
            _configuration.SecurityConfiguration.MinimumCertificateKeySize = 1024;
            Logger.Information($"Minimum certificate key size set to {_configuration.SecurityConfiguration.MinimumCertificateKeySize}");

            // Application certificate
            _configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier();
            _configuration.SecurityConfiguration.ApplicationCertificate.StoreType = _opcOwnCertStoreType;
            _configuration.SecurityConfiguration.ApplicationCertificate.StorePath = _opcOwnCertStorePath;
            _configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = _configuration.ApplicationName;
            Logger.Information($"Application Certificate store type is: {_configuration.SecurityConfiguration.ApplicationCertificate.StoreType}");
            Logger.Information($"Application Certificate store path is: {_configuration.SecurityConfiguration.ApplicationCertificate.StorePath}");
            Logger.Information($"Application Certificate subject name is: {_configuration.SecurityConfiguration.ApplicationCertificate.SubjectName}");

            // handle cert validation
            if (_autoAcceptCerts)
            {
                Logger.Warning("WARNING: Automatically accepting certificates. This is a security risk.");
                _configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            }
            _configuration.CertificateValidator = new Opc.Ua.CertificateValidator();
            _configuration.CertificateValidator.CertificateValidation += new Opc.Ua.CertificateValidationEventHandler(CertificateValidator_CertificateValidation);

            //// update security information
            await _configuration.CertificateValidator.Update(_configuration.SecurityConfiguration);

            // Use existing certificate, if it is there.
            X509Certificate2 certificate = await _configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
            if (certificate == null)
            {
                Logger.Information($"No existing Application certificate found. Create a self-signed Application certificate valid from yesterday for {CertificateFactory.defaultLifeTime} months,");
                Logger.Information($"with a {CertificateFactory.defaultKeySize} bit key and {CertificateFactory.defaultHashSize} bit hash.");
                certificate = CertificateFactory.CreateCertificate(
                    _configuration.SecurityConfiguration.ApplicationCertificate.StoreType,
                    _configuration.SecurityConfiguration.ApplicationCertificate.StorePath,
                    null,
                    _configuration.ApplicationUri,
                    _configuration.ApplicationName,
                    _configuration.ApplicationName,
                    null,
                    CertificateFactory.defaultKeySize,
                    DateTime.UtcNow - TimeSpan.FromDays(1),
                    CertificateFactory.defaultLifeTime,
                    CertificateFactory.defaultHashSize,
                    false,
                    null,
                    null
                    );
                _configuration.SecurityConfiguration.ApplicationCertificate.Certificate = certificate ?? throw new Exception("OPC UA application certificate can not be created! Cannot continue without it!");
            }
            else
            {
                Logger.Information("Application certificate found in Application Certificate Store");
            }
            _configuration.ApplicationUri = Utils.GetApplicationUriFromCertificate(certificate);
            Logger.Information($"Application certificate is for Application URI '{_configuration.ApplicationUri}', Application '{_configuration.ApplicationName} and has Subject '{_configuration.ApplicationName}'");

            // We make the default reference stack behavior configurable to put our own certificate into the trusted peer store.
            // Note: SecurityConfiguration.AddAppCertToTrustedStore only works for Application instance objects, which we do not have.
            if (_trustMyself)
            {
                // Ensure it is trusted
                try
                {
                    ICertificateStore store = _configuration.SecurityConfiguration.TrustedPeerCertificates.OpenStore();
                    if (store == null)
                    {
                        Logger.Warning($"Can not open trusted peer store. StorePath={_configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath}");
                    }
                    else
                    {
                        try
                        {
                            Logger.Information($"Adding server certificate to trusted peer store. StorePath={_configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath}");
                            X509Certificate2 publicKey = new X509Certificate2(certificate.RawData);
                            await store.Add(publicKey);
                        }
                        finally
                        {
                            store.Close();
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Fatal(e, $"Can not add server certificate to trusted peer store. StorePath={_configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath})");
                }
            }
            else
            {
                Logger.Warning("Server certificate is not added to trusted peer store.");
            }

            //
            // TransportConfigurations
            //
            _configuration.TransportQuotas = new TransportQuotas();

            // add default client configuration
            _configuration.ClientConfiguration = new ClientConfiguration();

            // validate the configuration now
            await _configuration.Validate(_configuration.ApplicationType);
            return _configuration;
        }

        /// <summary>
        /// Event handler to validate certificates.
        /// </summary>
        private static void CertificateValidator_CertificateValidation(Opc.Ua.CertificateValidator validator, Opc.Ua.CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = _autoAcceptCerts;
                if (_autoAcceptCerts)
                {
                    Logger.Information($"Accepting Certificate: {e.Certificate.Subject}");
                }
                else
                {
                    Logger.Information($"Rejecting Certificate: {e.Certificate.Subject}");
                }
            }
        }

        /// <summary>
        /// Event handler to log OPC UA stack trace messages into own logger.
        /// </summary>
        private static void LoggerOpcUaTraceHandler(object sender, TraceEventArgs e)
        {
            // return fast if no trace needed
            if ((e.TraceMask & _opcStackTraceMask) == 0)
            {
                return;
            }

            // e.Exception and e.Message are always null

            // format the trace message
            string message = string.Empty;
            message = string.Format(e.Format, e.Arguments).Trim();
            message = "OPC: " + message;

            // map logging level
            if ((e.TraceMask & OpcTraceToLoggerVerbose) != 0)
            {
                Logger.Verbose(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerDebug) != 0)
            {
                Logger.Debug(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerInformation) != 0)
            {
                Logger.Information(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerWarning) != 0)
            {
                Logger.Warning(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerError) != 0)
            {
                Logger.Error(message);
                return;
            }
            if ((e.TraceMask & OpcTraceToLoggerFatal) != 0)
            {
                Logger.Fatal(message);
                return;
            }
            return;
        }

        private static string _stationHostname = $"{Utils.GetHostName()}";
        private static bool _trustMyself = false;
        private static int _opcStackTraceMask = 0;

        private static string _opcOwnCertStoreType = X509Store;
        private static string _opcOwnCertStorePath = OpcOwnCertX509StorePathDefault;
        private static string _opcTrustedCertStoreType = Directory;
        private static string _opcTrustedCertStorePath = OpcTrustedCertDirectoryStorePathDefault;
        private static string _opcRejectedCertStoreType = Directory;
        private static string _opcRejectedCertStorePath = OpcRejectedCertDirectoryStorePathDefault;
        private static string _opcIssuerCertStoreType = Directory;
        private static string _opcIssuerCertStorePath = OpcIssuerCertDirectoryStorePathDefault;

        private static ApplicationConfiguration _configuration;
        private static bool _autoAcceptCerts = false;
    }
}
