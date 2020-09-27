
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CfMes
{
    using Mono.Options;
    using Opc.Ua;
    using Serilog;
    using Serilog.Core;
    using System.Data;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using static OpcApplicationConfiguration;
    using static Program;
    using static ShiftControl;

    public enum StationStatus : int
    {
        Ready = 0,
        WorkInProgress = 1,
        Done = 2,
        Discarded = 3,
        Fault = 4
    }

    public class Station
    {
        public string EndpointUrl { get; set; } = string.Empty;

        public StationStatus Status { get; set; }

        public int CurrentShift { get; set; }

        public ulong ProductSerialNumber { get; set; }

        public bool IsReady => Status == StationStatus.Ready;

        public bool IsFault => Status == StationStatus.Fault;

        public bool IsDone => Status == StationStatus.Done;

        public bool IsInProgress => Status == StationStatus.WorkInProgress;

        public bool IsDisconnected => _reconnectHandler != null;

        protected ILogger _logger;

        public Station(ILogger logger,  string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken ct)
        {
            _logger = logger;
            Status = StationStatus.Ready;
            ProductSerialNumber = 1;
            EndpointUrl = endpointUrl;
            _mesConfiguration = mesConfiguration;
            _endpoint = new ConfiguredEndpoint();
            _shutdownToken = ct;
            try
            {
                _endpoint.EndpointUrl = new Uri(endpointUrl);
            }
            catch (Exception e)
            {
                _logger.Fatal(e, "The endpoint URL '{_endpointUrl}' has an invalid format!");
                throw e;
            }
        }

        /// <summary>
        /// Connect to stations OPC endpoint and start monitoring the status node.
        /// </summary>
        public async Task ConnectStationOpcServerAsync(MonitoredItemNotificationEventHandler handler)
        {
            while (true)
            {
                // cancel if requested
                if (_shutdownToken.IsCancellationRequested)
                {
                    return;
                }

                // establish a session to the endpoint
                if (!CreateSession())
                {
                    _logger.Fatal($"Failed to create session to endpoint at {EndpointUrl}! Wait {RECONNECT_DELAY} seconds and retry...");
                    await Task.Delay(RECONNECT_DELAY, _shutdownToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    // initialize the station status and fetch current product serial number
                    await StationControl.WaitAsync().ConfigureAwait(false);
                    Status = (StationStatus)_session.ReadValue(STATIONSTATUS_NODEID).Value;
                    ProductSerialNumber = (ulong)_session.ReadValue(PRODUCTSERIALNUMBER_NODEID).Value;

                    // start monitoring the status node
                    if (!StartStationStatusMonitoring(handler))
                    {
                        _logger.Error($"Failed to create monitored item for station status at {EndpointUrl}! Wait {RECONNECT_DELAY} seconds and retry...");
                        await Task.Delay(RECONNECT_DELAY, _shutdownToken).ConfigureAwait(false);
                        continue;
                    }
                    break;
                }
                finally
                {
                    StationControl.Release();
                }
            }
        }

        /// <summary>
        /// Create an OPC session to the stations endpoint.
        /// </summary>
        /// <returns></returns>
        private bool CreateSession()
        {
            // check if session exists already
            if (_session != null)
            {
                return true;
            }

            try
            {
                _logger.Information($"Create session to endpoint {EndpointUrl}.");
                _session = Session.Create(
                    _mesConfiguration,
                    _endpoint,
                    true,
                    _mesConfiguration.ApplicationName,
                    CONNECT_TIMEOUT,
                    new UserIdentity(new AnonymousIdentityToken()),
                    null).Result;
            }
            catch (Exception e)
            {
                _logger.Fatal(e, $"Failed to create session to endpoint {EndpointUrl}!");
            }
            if (_session != null)
            {
                _session.KeepAlive += new KeepAliveEventHandler((sender, e) => Client_KeepAlive(sender, e));
            }
            else
            {
                _logger.Error($"Could not create session to endpoint {EndpointUrl}!");
                return false;
            }
            _logger.Information($"Session to endpoint {EndpointUrl} established.");
            return true;
        }

        /// <summary>
        /// Start monitoring of the station status node.
        /// </summary>
        /// <param name="handler"></param>
        public bool StartStationStatusMonitoring(MonitoredItemNotificationEventHandler handler)
        {
            if (_session != null)
            {
                try
                {
                    _logger.Information($"Start monitoring status node on endpoint {EndpointUrl}.");
                    // access the default subscription, add it to the session and only create it if successful
                    _subscription = _session.DefaultSubscription;
                    if (_session.AddSubscription(_subscription))
                    {
                        _subscription.Create();
                    }

                    // add the new monitored item.
                    MonitoredItem monitoredItem = new MonitoredItem(_subscription.DefaultItem);
                    if (monitoredItem != null)
                    {
                        // Set monitored item attributes
                        // StartNodeId = NodeId to be monitored
                        // AttributeId = which attribute of the node to monitor (in this case the value)
                        // MonitoringMode = When sampling is enabled, the Server samples the item.
                        // In addition, each sample is evaluated to determine if
                        // a Notification should be generated. If so, the
                        // Notification is queued. If reporting is enabled,
                        // the queue is made available to the Subscription for transfer
                        NodeId nodeId = new NodeId(STATIONSTATUS_NODEID);
                        monitoredItem.StartNodeId = nodeId;
                        monitoredItem.AttributeId = Attributes.Value;
                        monitoredItem.DisplayName = nodeId.Identifier.ToString();
                        monitoredItem.MonitoringMode = MonitoringMode.Reporting;
                        monitoredItem.SamplingInterval = 0;
                        monitoredItem.QueueSize = 0;
                        monitoredItem.DiscardOldest = true;

                        monitoredItem.Notification += handler;
                        _subscription.AddItem(monitoredItem);
                        _subscription.ApplyChanges();
                        _logger.Information($"Now monitoring status node on endpoint {EndpointUrl}.");
                        return true;
                    }
                    else
                    {
                        _logger.Error("Could not create subscription to monitor station status!");
                    }
                }
                catch (Exception e)
                {
                    _logger.Fatal(e, "Could not create subscription to monitor station status!");
                }
            }
            return false;
        }

        /// <summary>
        /// OPC keep alive handler.
        /// </summary>
        private void Client_KeepAlive(Session sender, KeepAliveEventArgs e)
        {
            if (e.Status != null && ServiceResult.IsNotGood(e.Status))
            {
                _logger.Warning($"Endpoint: {EndpointUrl} Status: {e.Status}, Outstanding requests: {sender.OutstandingRequestCount},  Defunct requests: {sender.DefunctRequestCount}");

                if (_reconnectHandler == null)
                {
                    _logger.Information($"--- RECONNECTING to endpoint {EndpointUrl}  ---");
                    _reconnectHandler = new SessionReconnectHandler();
                    _reconnectHandler.BeginReconnect(sender, RECONNECT_PERIOD, Client_ReconnectComplete);
                }
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!Object.ReferenceEquals(sender, _reconnectHandler))
            {
                return;
            }

            _session = _reconnectHandler.Session;
            _reconnectHandler.Dispose();
            _reconnectHandler = null;

            _logger.Information($"--- RECONNECTED to endpoint {EndpointUrl}  ---");
        }

        /// <summary>
        /// Calls the Execute method in the station.
        /// </summary>
        public void Execute()
        {
            bool callSuccessfull = false;
            int retryCount = 1;

            VariantCollection inputArgumentsExecute = new VariantCollection()
            {
                CurrentShift,
                ProductSerialNumber
            };

            while (!callSuccessfull && !_shutdownToken.IsCancellationRequested)
            {
                try
                {
                    if (_reconnectHandler != null)
                    {
                        _logger.Debug($"In reconnect. Wait {RECONNECT_PERIOD} msec till retry calling Execute method.");
                        Task.Delay(RECONNECT_PERIOD, _shutdownToken).Wait();
                        continue;
                    }
                    if (retryCount++ > 1)
                    {
                        _logger.Warning($"Retry {retryCount}th time to call Execute method on endpoint {EndpointUrl}.");
                    }
                    CallMethodRequestCollection requests = new CallMethodRequestCollection();
                    CallMethodRequest request = new CallMethodRequest
                    {
                        ObjectId = new NodeId("Methods", 2),
                        MethodId = new NodeId("Execute", 2),
                    };
                    request.InputArguments = inputArgumentsExecute;
                    requests.Add(request);
                    ResponseHeader responseHeader = _session.Call(null, requests, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos);
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        _logger.Error($"Execute call was not successfull on endpoint URL {EndpointUrl} (status: '{results[0].StatusCode}'. Retry...");
                    }
                    else
                    {
                        callSuccessfull = true;
                    }
                }
                catch (Exception e)
                {
                    _logger.Fatal($"Exception when calling Execute method on endpoint URL {EndpointUrl}. Retry...");
                    _logger.Fatal(e, "Exception details:");
                    Task.Delay(10000, _shutdownToken).Wait();
                }
            }
        }

        /// <summary>
        /// Calls the OpenPressureReleaseValve method in the station.
        /// </summary>
        public void OpenPressureReleaseValve()
        {
            bool callSuccessfull = false;
            int retryCount = 1;

            VariantCollection inputArguments = new VariantCollection();

            while (!callSuccessfull && !_shutdownToken.IsCancellationRequested)
            {
                try
                {
                    if (_reconnectHandler != null)
                    {
                        _logger.Debug($"In reconnect. Wait {RECONNECT_PERIOD} msec till retry calling OpenPressureReleaseValve method.");
                        Task.Delay(RECONNECT_PERIOD, _shutdownToken).Wait();
                        continue;
                    }
                    if (retryCount++ > 1)
                    {
                        _logger.Warning($"Retry {retryCount}th time to call OpenPressureReleaseValve method on endpoint {EndpointUrl}.");
                    }
                    CallMethodRequestCollection requests = new CallMethodRequestCollection();
                    CallMethodRequest request = new CallMethodRequest
                    {
                        ObjectId = new NodeId("Methods", 2),
                        MethodId = new NodeId("OpenPressureReleaseValve", 2),
                    };
                    request.InputArguments = inputArguments;
                    requests.Add(request);
                    ResponseHeader responseHeader = _session.Call(null, requests, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos);
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        _logger.Error($"OpenPressureReleaseValve call was not successfull on endpoint URL {EndpointUrl} (status: '{results[0].StatusCode}'");
                    }
                    else
                    {
                        callSuccessfull = true;
                    }
                }
                catch (Exception e)
                {
                    _logger.Fatal($"Exception when calling OpenPressureReleaseValve method on endpoint URL {EndpointUrl}");
                    _logger.Fatal(e, "Exception details:");
                    Task.Delay(10000, _shutdownToken).Wait();
                }
            }
        }

        /// <summary>
        /// Calls the Reset method in the station. Put the station in ready state.
        /// </summary>
        public void Reset()
        {
            bool callSuccessfull = false;
            int retryCount = 1;

            VariantCollection inputArguments = new VariantCollection();

            while (!callSuccessfull && !_shutdownToken.IsCancellationRequested)
            {
                try
                {
                    if (_reconnectHandler != null)
                    {
                        _logger.Debug($"In reconnect. Wait {RECONNECT_PERIOD} msec till retry calling Reset method.");
                        Task.Delay(RECONNECT_PERIOD, _shutdownToken).Wait();
                        continue;
                    }
                    if (retryCount++ > 1)
                    {
                        _logger.Warning($"Retry {retryCount}th time to call Reset method on endpoint {EndpointUrl}.");
                    }
                    CallMethodRequestCollection requests = new CallMethodRequestCollection();
                    CallMethodRequest request = new CallMethodRequest
                    {
                        ObjectId = new NodeId("Methods", 2),
                        MethodId = new NodeId("Reset", 2),
                    };
                    request.InputArguments = inputArguments;
                    requests.Add(request);
                    ResponseHeader responseHeader = _session.Call(null, requests, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos);
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        _logger.Error($"Reset call was not successfull on endpoint URL {EndpointUrl} (status: '{results[0].StatusCode}'");
                    }
                    else
                    {
                        callSuccessfull = true;
                    }
                }
                catch (Exception e)
                {
                    _logger.Fatal($"Exception when calling Reset method on endpoint URL {EndpointUrl}");
                    _logger.Fatal(e, "Exception details:");
                    Task.Delay(10000, _shutdownToken).Wait();
                }
            }
        }

        private const uint CONNECT_TIMEOUT = 60000;
        private const int RECONNECT_DELAY = 10 * 1000;
        private const string STATIONSTATUS_NODEID = "ns=2;s=Status";
        private const string PRODUCTSERIALNUMBER_NODEID = "ns=2;s=ProductSerialNumber";
        private const int RECONNECT_PERIOD = 10 * 1000;
        private readonly ConfiguredEndpoint _endpoint = null;
        private Session _session = null;
        private Subscription _subscription = null;
        private readonly ApplicationConfiguration _mesConfiguration = null;
        private SessionReconnectHandler _reconnectHandler = null;
        private readonly CancellationToken _shutdownToken;
    }

    public class AssemblyStation : Station
    {
        public AssemblyStation(ILogger logger, string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken shutdownToken) : base(logger, endpointUrl, mesConfiguration, shutdownToken)
        {
            _logger.Information($"AssemblyStation URL is: {endpointUrl}");
        }

        /// <summary>
        /// Connect to the station and reset it to Ready status.
        /// </summary>
        public async Task ConnectStationAsync()
        {
            await ConnectStationOpcServerAsync(new MonitoredItemNotificationEventHandler(MonitoredItem_Notification)).ConfigureAwait(false);
            Reset();
        }

        private void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs eventArgs)
        {
            try
            {
                StationControl.Wait();
                MonitoredItemNotification change = eventArgs.NotificationValue as MonitoredItemNotification;
                Status = (StationStatus)change.Value.Value;

                _logger.Debug($"AssemblyStation: status changed to {Status}");

                if (!_firstNotification)
                {
                    // now check what the status is
                    switch (Status)
                    {
                        case StationStatus.Ready:
                            // build the next product by calling execute with new serial number
                            ProductSerialNumber++;
                            // check if shift has changed

                            Execute();
                            _logger.Information($"AssemblyStation: now building #{ProductSerialNumber}");
                            break;

                        case StationStatus.Discarded:
                            // product was automatically discarded by the station, reset
                            _logger.Debug($"AssemblyStation: #{ProductSerialNumber} discarded");
                            Reset();
                            break;

                        case StationStatus.Fault:
                            Task.Run(async () =>
                            {
                                // station is at fault state, wait some time to simulate manual intervention before reseting
                                _logger.Information("AssemblyStation: <<Fault detected>>");
                                await Task.Delay(FAULT_DELAY).ConfigureAwait(false);
                                _logger.Information("AssemblyStation: <<Fix Fault>>");
                                Reset();
                            });
                            break;

                        case StationStatus.WorkInProgress:
                        case StationStatus.Done:
                            break;

                        default:
                            _logger.Error("Argument error: Invalid station status type received!");
                            break;
                    }
                }
                _firstNotification = false;
            }
            catch (Exception e)
            {
                _logger.Fatal(e, "Error processing monitored item notification in AssemblyStation");
            }
            finally
            {
                StationControl.Release();
            }
        }
        private static bool _firstNotification = true;
    }

    public class TestStation : Station
    {
        public TestStation(ILogger logger, string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken shutdownToken) : base(logger, endpointUrl, mesConfiguration, shutdownToken)
        {
            _logger.Information($"TestStation URL is: {endpointUrl}");
        }

        /// <summary>
        /// Connect to the station.
        /// </summary>
        public async Task ConnectStationAsync()
        {
            await ConnectStationOpcServerAsync(new MonitoredItemNotificationEventHandler(MonitoredItem_Notification)).ConfigureAwait(false);
        }

        /// <summary>
        /// Station status change handler.
        /// </summary>
        private void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs eventArgs)
        {
            try
            {
                StationControl.Wait();
                MonitoredItemNotification change = eventArgs.NotificationValue as MonitoredItemNotification;
                Status = (StationStatus)change.Value.Value;

                _logger.Debug($"TestStation: status changed to {Status}");

                if (!_firstNotification)
                {
                    switch (Status)
                    {
                        case StationStatus.Done:
                            _logger.Debug($"TestStation: #{ProductSerialNumber} testing passed");
                            break;

                        case StationStatus.Discarded:
                            _logger.Debug($"TestStation:  #{ProductSerialNumber} testing failed -> discard");
                            Reset();
                            break;

                        case StationStatus.Fault:
                            {
                                Task.Run(async () =>
                                {
                                    _logger.Information("TestStation: <<Fault detected>>");
                                    await Task.Delay(FAULT_DELAY).ConfigureAwait(false);
                                    _logger.Information("TestStation: <<Fix Fault>>");
                                    Reset();
                                });
                            }
                            break;

                        case StationStatus.Ready:
                        case StationStatus.WorkInProgress:
                            break;

                        default:
                            {
                                _logger.Error("Argument error: Invalid station status type received!");
                                return;
                            }
                    }
                }
                _firstNotification = false;
            }
            catch (Exception e)
            {
                _logger.Fatal(e, "Exception: Error processing monitored item notification in TestStation");
            }
            finally
            {
                StationControl.Release();
            }
        }
        private static bool _firstNotification = true;
    }

    public class PackagingStation : Station
    {
        public PackagingStation(ILogger logger, string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken shutdownToken) : base(logger, endpointUrl, mesConfiguration, shutdownToken)
        {
            _logger.Information($"PackagingStation URL is: {endpointUrl}");
        }

        /// <summary>
        /// Connect to the station.
        /// </summary>
        public async Task ConnectStationAsync()
        {
            await ConnectStationOpcServerAsync(new MonitoredItemNotificationEventHandler(MonitoredItem_Notification)).ConfigureAwait(false);
        }

        /// <summary>
        /// Station status change handler.
        /// </summary>
        private void MonitoredItem_Notification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs eventArgs)
        {
            try
            {
                StationControl.Wait();
                MonitoredItemNotification change = eventArgs.NotificationValue as MonitoredItemNotification;
                Status = (StationStatus)change.Value.Value;

                _logger.Debug($"PackagingStation: status changed to {Status}");

                if (!_firstNotification)
                {
                    switch (Status)
                    {
                        case StationStatus.Ready:
                        case StationStatus.WorkInProgress:
                            break;

                        case StationStatus.Done:
                            _logger.Information($"PackagingStation: #{ProductSerialNumber} completed successfully");
                            Reset();
                            break;

                        case StationStatus.Discarded:
                            _logger.Information($"PackagingStation: #{ProductSerialNumber} completed, but not good");
                            Reset();
                            break;

                        case StationStatus.Fault:
                            {
                                Task.Run(async () =>
                                {
                                    _logger.Information("PackagingStation: <<Fault detected>>");
                                    await Task.Delay(FAULT_DELAY).ConfigureAwait(false);
                                    _logger.Information("PackagingStation: <<Fix Fault>>");
                                    Reset();
                                });
                            }
                            break;

                        default:
                            _logger.Error("Argument error: Invalid station status type received!");
                            break;
                    }
                }
                _firstNotification = false;
            }
            catch (Exception e)
            {
                _logger.Fatal(e, "Exception: Error processing monitored item notification in PackagingStation");
            }
            finally
            {
                StationControl.Release();
            }
        }
        private static bool _firstNotification = true;
    }

    public class Program
    {
        public const int FAULT_DELAY = 60 * 1000;

        public static string AssemblyStationEndpointUrl { get; set; } = $"opc.tcp://{Utils.GetHostName()}:51210";

        public static string TestStationEndpointUrl { get; set; } = $"opc.tcp://{Utils.GetHostName()}:51211";

        public static string PackagingStationEndpointUrl { get; set; } = $"opc.tcp://{Utils.GetHostName()}:51212";

        public static SemaphoreSlim StationControl;
        public static AssemblyStation AssemblyStation = null;
        public static TestStation TestStation = null;
        public static PackagingStation PackagingStation = null;

        public static IShiftControl ShiftController;

        /// <summary>
        /// Synchronous main method of the app.
        /// </summary>
        public static void Main(string[] args)
        {
#if DEBUG
            if (args.Any(a => a.ToLowerInvariant().Contains("wfd") ||
                    a.ToLowerInvariant().Contains("waitfordebugger")))
            {
                Console.WriteLine("Waiting for debugger being attached...");
                while (!Debugger.IsAttached)
                {
                    Thread.Sleep(1000);
                }
                Console.WriteLine("Debugger attached.");
            }
#endif

            MainAsync(args).Wait();
        }

        /// <summary>
        /// Asynchronous part of the main method of the app.
        /// </summary>
        public static async Task MainAsync(string[] args)
        {
            var quitEvent = new ManualResetEvent(false);
            var shouldShowHelp = false;

            _shutdownSource = new CancellationTokenSource();
            _shutdownToken = _shutdownSource.Token;

            // command line options
            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                // endpoint configuration options
                { "as|assemblystation=", $"the endpoint of the assemblystation\nDefault: '{AssemblyStationEndpointUrl}'", (string s) => AssemblyStationEndpointUrl = s },
                { "ts|teststation=", $"the endpoint of the teststation\nDefault: '{TestStationEndpointUrl}'", (string s) => TestStationEndpointUrl = s },
                { "ps|packagingstation=", $"the endpoint of the packagingstation\nDefault: '{PackagingStationEndpointUrl}'", (string s) => PackagingStationEndpointUrl = s },

                // shift configuration
                { "fs|firstshiftstart=", $"time the work starts every day (24hr format: hhmm)\nDefault: '{_firstShiftStartTime/100:00}{_firstShiftStartTime%100:00}'", (int i) => {
                        if (i >= FIRST_SHIFT_START_MIN && i <= FIRST_SHIFT_START_MAX && i % 100 < 60)
                        {
                            _firstShiftStartTime = i;
                            _shiftEnabled = true;
                        }
                        else
                        {
                            throw new Mono.Options.OptionException($"The start of the first shift in a day must specify a time between {FIRST_SHIFT_START_MIN} and {FIRST_SHIFT_START_MAX}", "firstshiftstart");
                        }                    }
                },
                { "sl|shiftlength=", $"shift length in minutes\nMin: {SHIFT_LENGTH_MINUTES_MIN}\nMax: {SHIFT_LENGTH_MINUTES_MAX}\nDefault: '{_shiftLengthInMinutes}'", (int i) => {
                        if (i >= SHIFT_LENGTH_MINUTES_MIN && i <= SHIFT_LENGTH_MINUTES_MAX)
                        {
                            _shiftLengthInMinutes = i;
                            _shiftEnabled = true;
                        }
                        else
                        {
                            throw new Mono.Options.OptionException($"The shift length must fit in a day and must be between {SHIFT_LENGTH_MINUTES_MIN} and {SHIFT_LENGTH_MINUTES_MAX} minutes.", "shiftlength");
                        }
                    }
                },
                { "ss|shiftshouldstart=", $"percent when we still should start a shift\nMin: {SHIFT_SHOULD_START_LIMIT_PERCENT_MIN}\nMax: {SHIFT_SHOULD_START_LIMIT_PERCENT_MAX}\nDefault: '{_shiftShouldStartLimitPercent}'", (double d) => {
                        if (d >= SHIFT_SHOULD_START_LIMIT_PERCENT_MIN && d <= SHIFT_SHOULD_START_LIMIT_PERCENT_MAX)
                        {
                            _shiftShouldStartLimitPercent = d;
                            _shiftEnabled = true;
                        }
                        else
                        {
                            throw new Mono.Options.OptionException($"The shift still should start limit should be between {SHIFT_SHOULD_START_LIMIT_PERCENT_MIN} and {SHIFT_SHOULD_START_LIMIT_PERCENT_MAX}", "shiftshouldstart");
                        }
                    }
                },
                { "sc|shiftcount=", $"number of shifts per day\nDefault: '{_shiftCount}'", (int i) => {
                        if (i >= SHIFT_COUNT_MIN && i <= SHIFT_COUNT_MAX)
                        {
                            _shiftCount = i;
                            _shiftEnabled = true;
                        }
                        else
                        {
                            throw new Mono.Options.OptionException($"The shift count supported per work day must be between {SHIFT_COUNT_MIN} and {SHIFT_COUNT_MAX}", "shiftcount");
                        }
                    }
                },
                { "dw|daysperweek=", $"number of working days per week starting monday\nDefault: '{_daysPerWeek}'", (int i) => {
                        if (i >= DAYS_PER_WEEK_MIN && i <= DAYS_PER_WEEK_MAX)
                        {
                            _daysPerWeek = i;
                            _shiftEnabled = true;
                        }
                        else
                        {
                            throw new Mono.Options.OptionException($"The work week has max {DAYS_PER_WEEK_MAX} days and at least {DAYS_PER_WEEK_MIN} day.", "daysperweek");
                        }
                    }
                },

                // OPC stack trace settings
                { "lf|logfile=", $"the filename of the logfile to use.\nDefault: '{_logFileName}'", (string l) => _logFileName = l },
                { "ll|loglevel=", "the loglevel to use (allowed: fatal, error, warn, info, debug, verbose).\nDefault: info", (string l) => {
                        List<string> logLevels = new List<string> {"fatal", "error", "warn", "info", "debug", "verbose"};
                        if (logLevels.Contains(l.ToLowerInvariant()))
                        {
                            _logLevel = l.ToLowerInvariant();
                        }
                        else
                        {
                            throw new OptionException("The loglevel must be one of: fatal, error, warn, info, debug, verbose", "loglevel");
                        }
                    }
                },
                { "aa|autoaccept", $"auto accept station server certificates\nDefault: '{AutoAcceptCerts}'", a => AutoAcceptCerts = a != null },
                { "to|trustowncert", $"the cfmes certificate is put into the trusted certificate store automatically.\nDefault: {TrustMyself}", t => TrustMyself = t != null },

                // cert store options
                { "at|appcertstoretype=", $"the own application cert store type. \n(allowed values: Directory, X509Store)\nDefault: '{OpcOwnCertStoreType}'", (string s) => {
                        if (s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) || s.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
                        {
                            OpcOwnCertStoreType = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? CertificateStoreType.X509Store : CertificateStoreType.Directory;
                            OpcOwnCertStorePath = s.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase) ? OpcOwnCertX509StorePathDefault : OpcOwnCertDirectoryStorePathDefault;
                        }
                        else
                        {
                            throw new OptionException();
                        }
                    }
                },

                { "ap|appcertstorepath=", "the path where the own application cert should be stored\nDefault (depends on store type):\n" +
                        $"X509Store: '{OpcOwnCertX509StorePathDefault}'\n" +
                        $"Directory: '{OpcOwnCertDirectoryStorePathDefault}'", (string s) => OpcOwnCertStorePath = s
                },

                { "tp|trustedcertstorepath=", $"the path of the trusted cert store\nDefault '{OpcTrustedCertDirectoryStorePathDefault}'", (string s) => OpcTrustedCertStorePath = s
                },

                { "rp|rejectedcertstorepath=", $"the path of the rejected cert store\nDefault '{OpcRejectedCertDirectoryStorePathDefault}'", (string s) => OpcRejectedCertStorePath = s
                },

                { "ip|issuercertstorepath=", $"the path of the trusted issuer cert store\nDefault '{OpcIssuerCertDirectoryStorePathDefault}'", (string s) => OpcIssuerCertStorePath = s
                },

                { "csr", $"show data to create a certificate signing request\nDefault '{ShowCreateSigningRequestInfo}'", c => ShowCreateSigningRequestInfo = c != null
                },

                { "ab|applicationcertbase64=", "update/set this applications certificate with the certificate passed in as bas64 string", (string s) => NewCertificateBase64String = s
                },
                { "af|applicationcertfile=", "update/set this applications certificate with the certificate file specified", (string s) =>
                    {
                        if (File.Exists(s))
                        {
                            NewCertificateFileName = s;
                        }
                        else
                        {
                            throw new OptionException("The file '{s}' does not exist.", "applicationcertfile");
                        }
                    }
                },

                { "pb|privatekeybase64=", "initial provisioning of the application certificate (with a PEM or PFX fomat) requires a private key passed in as base64 string", (string s) => PrivateKeyBase64String = s
                },
                { "pk|privatekeyfile=", "initial provisioning of the application certificate (with a PEM or PFX fomat) requires a private key passed in as file", (string s) =>
                    {
                        if (File.Exists(s))
                        {
                            PrivateKeyFileName = s;
                        }
                        else
                        {
                            throw new OptionException("The file '{s}' does not exist.", "privatekeyfile");
                        }
                    }
                },

                { "cp|certpassword=", "the optional password for the PEM or PFX or the installed application certificate", (string s) => CertificatePassword = s
                },

                { "tb|addtrustedcertbase64=", "adds the certificate to the applications trusted cert store passed in as base64 string (multiple strings supported)", (string s) => TrustedCertificateBase64Strings = ParseListOfStrings(s)
                },
                { "tf|addtrustedcertfile=", "adds the certificate file(s) to the applications trusted cert store passed in as base64 string (multiple filenames supported)", (string s) => TrustedCertificateFileNames = ParseListOfFileNames(s, "addtrustedcertfile")
                },

                { "ib|addissuercertbase64=", "adds the specified issuer certificate to the applications trusted issuer cert store passed in as base64 string (multiple strings supported)", (string s) => IssuerCertificateBase64Strings = ParseListOfStrings(s)
                },
                { "if|addissuercertfile=", "adds the specified issuer certificate file(s) to the applications trusted issuer cert store (multiple filenames supported)", (string s) => IssuerCertificateFileNames = ParseListOfFileNames(s, "addissuercertfile")
                },

                { "rb|updatecrlbase64=", "update the CRL passed in as base64 string to the corresponding cert store (trusted or trusted issuer)", (string s) => CrlBase64String = s
                },
                { "uc|updatecrlfile=", "update the CRL passed in as file to the corresponding cert store (trusted or trusted issuer)", (string s) =>
                    {
                        if (File.Exists(s))
                        {
                            CrlFileName = s;
                        }
                        else
                        {
                            throw new OptionException("The file '{s}' does not exist.", "updatecrlfile");
                        }
                    }
                },

                { "rc|removecert=", "remove cert(s) with the given thumbprint(s) (multiple thumbprints supported)", (string s) => ThumbprintsToRemove = ParseListOfStrings(s)
                },

                // misc
                { "h|help", "show this message and exit", h => shouldShowHelp = h != null },
            };

            List<string> extraArgs = new List<string>();
            try
            {
                // parse the command line
                extraArgs = options.Parse(args);
                if (extraArgs.Contains("wfd"))
                {
                    extraArgs.Remove("wdf");
                }
            }
            catch (Mono.Options.OptionException e)
            {
                // initialize logging
                InitLogging();

                // show message
                _logger.Fatal(e, "Error in command line options");

                // show usage
                Usage(options);
                return;
            }

            // initialize logging
            InitLogging();

            // check args
            if (extraArgs.Count > 1 || shouldShowHelp)
            {
                // show usage
                Usage(options);
                return;
            }
            _logger.Information($"{Assembly.GetEntryAssembly().GetName().Name} V{ThisAssembly.AssemblyVersion}");

            // correct days per week
            if (_shiftEnabled && _daysPerWeek == 0)
            {
                _daysPerWeek = 5;
            }

            try
            {
                ShiftController = _daysPerWeek == 0 ? null : new ShiftControl(_logger, _daysPerWeek, _shiftCount, _firstShiftStartTime, _shiftLengthInMinutes, _shiftShouldStartLimitPercent);
            }
            catch (Exception e)
            {
                _logger.Fatal(e.Message);
                return;
            }

            try
            {
                StationControl = new SemaphoreSlim(1);

                // allow canceling the connection process
                try
                {
                    Console.CancelKeyPress += (sender, eArgs) =>
                    {
                        _shutdownSource.Cancel();
                        quitEvent.Set();
                        eArgs.Cancel = true;
                    };
                }
                catch
                {
                }
                _logger.Information("Connectedfactory Manufacturing Execution System starting up. Press CTRL-C to exit.");

                // create stations
                OpcApplicationConfiguration mesOpcConfiguration = new OpcApplicationConfiguration();
                ApplicationConfiguration mesConfiguration = await mesOpcConfiguration.ConfigureAsync(_logger).ConfigureAwait(false);
                AssemblyStation = new AssemblyStation(_logger, AssemblyStationEndpointUrl, mesConfiguration, _shutdownToken);
                TestStation = new TestStation(_logger, TestStationEndpointUrl, mesConfiguration, _shutdownToken);
                PackagingStation = new PackagingStation(_logger, PackagingStationEndpointUrl, mesConfiguration, _shutdownToken);

                // connect to all servers.
                var stationConnections = new List<Task>
                {
                    Task.Run(async () => await AssemblyStation.ConnectStationAsync().ConfigureAwait(false)),
                    Task.Run(async () => await TestStation.ConnectStationAsync().ConfigureAwait(false)),
                    Task.Run(async () => await PackagingStation.ConnectStationAsync().ConfigureAwait(false))
                };

                try
                {
                    Task.WaitAll(stationConnections.ToArray());
                }
                catch
                {
                }

                // kick off the production process
                if (!_shutdownToken.IsCancellationRequested)
                {
                    await StationControl.WaitAsync().ConfigureAwait(false);

                    // start first production slot
                    StartProductionSlot(true);
                    StationControl.Release();
                }

                // wait for Ctrl-C
                quitEvent.WaitOne(Timeout.Infinite);
            }
            catch (Exception e)
            {
                _logger.Fatal(e, "MES failed unexpectedly!");
                StationControl = null;
                StationControl.Dispose();
            }
            _logger.Information("MES is exiting...");
        }

        private static void MesLogic(object state)
        {
            try
            {
                StationControl.Wait();

                // when the assembly station is done and the test station is ready
                // move the serial number (the product) to the test station and call
                // the method execute for the test station to start working, and
                // the reset method for the assembly to go in the ready state
                if (AssemblyStation.IsDone && TestStation.IsReady)
                {
                    _logger.Debug($"MES: moving #{AssemblyStation.ProductSerialNumber} from AssemblyStation to TestStation");
                    TestStation.ProductSerialNumber = AssemblyStation.ProductSerialNumber;
                    TestStation.Execute();
                    AssemblyStation.Reset();
                }

                // when the test station is done and the packaging station is ready
                // move the serial number (the product) to the packaging station and call
                // the method execute for the packaging station to start working, and
                // the reset method for the test to go in the ready state
                if (TestStation.IsDone && PackagingStation.IsReady)
                {
                    _logger.Debug($"MES: moving #{TestStation.ProductSerialNumber} from TestStation to PackagingStation");
                    PackagingStation.ProductSerialNumber = TestStation.ProductSerialNumber;
                    PackagingStation.Execute();
                    TestStation.Reset();
                }

                // sanity check stations
                if (PackagingStation.IsDone)
                {
                    PackagingStation.Reset();
                }
            }
            catch (Exception e)
            {
                _logger.Fatal(e, "Error in MES logic!");
            }
            finally
            {
                StationControl.Release();
                StartProductionSlot();
            }
        }

        /// <summary>
        /// Restart production slot.
        /// </summary>
        /// <param name="dueTime"></param>
        private static void StartProductionSlot(bool firstSlot = false)
        {
            // defer if there is any fault or any reconnect in progress
            while (true)
            {
                if (AssemblyStation.IsDisconnected || TestStation.IsDisconnected || PackagingStation.IsDisconnected)
                {
                    _logger.Debug($"One station is disconnected - Assembly: {AssemblyStation.IsDisconnected}, Test: {TestStation.IsDisconnected} Packaging: {PackagingStation.IsDisconnected}");
                    Task.Delay(PRODUCTION_SLOT_TIME, _shutdownToken).Wait();
                    continue;
                }
                if (AssemblyStation.IsFault || TestStation.IsFault || PackagingStation.IsFault)
                {
                    _logger.Debug($"One station faulted - Assembly: {AssemblyStation.IsFault}, Test: {TestStation.IsFault} Packaging: {PackagingStation.IsFault}");
                    Task.Delay(PRODUCTION_SLOT_TIME, _shutdownToken).Wait();
                    continue;
                }
                break;
            }

            // free resources
            _timer?.Dispose();

            // give up on shutdown
            if (_shutdownToken.IsCancellationRequested)
            {
                return;
            }

            // get and check shift
            int newShift = -1;
            int currentShift = -1;
            do
            {
                if (_daysPerWeek > 0 && currentShift != (newShift = ShiftController.CurrentShift(out DateTime nextShiftStart)))
                {
                    // wait till next shift starts
                    if (newShift == 0)
                    {
                        _logger.Information($"Wait till next shift starts at {nextShiftStart} ({(int)(nextShiftStart - DateTime.Now).TotalMinutes} minutes)");
                        Thread.Sleep((int)(nextShiftStart - DateTime.Now).TotalMilliseconds);
                    }
                }
            } while (newShift == 0);
            currentShift = newShift;

            // update shift in all stations
            AssemblyStation.CurrentShift = currentShift;
            TestStation.CurrentShift = currentShift;
            PackagingStation.CurrentShift = currentShift;

            // kick off production on the first slot
            if (firstSlot)
            {
                // continue production with the last product serial number
                AssemblyStation.ProductSerialNumber = Math.Max(AssemblyStation.ProductSerialNumber, Math.Max(TestStation.ProductSerialNumber, PackagingStation.ProductSerialNumber)) + 1;
                _logger.Information($"MES: Start production line by assembling product with product serial number #{AssemblyStation.ProductSerialNumber}");
                AssemblyStation.Execute();
            }
            _logger.Debug($"MES: Starting production slot {_productionSlot++} in shift {currentShift}");
            _timer = new Timer(MesLogic, null, PRODUCTION_SLOT_TIME, Timeout.Infinite);
        }

        /// <summary>
        /// Usage message.
        /// </summary>
        private static void Usage(Mono.Options.OptionSet options)
        {
            // show some app description message
            _logger.Information($"{Assembly.GetEntryAssembly().GetName().Name} V{ThisAssembly.AssemblyVersion} (Informational Version: {ThisAssembly.AssemblyInformationalVersion})");
            _logger.Information($"Usage: {Assembly.GetEntryAssembly().GetName().Name}.exe [<options>]");
            _logger.Information("");

            // output the options
            _logger.Information("Options:");
            StringBuilder stringBuilder = new StringBuilder();
            StringWriter stringWriter = new StringWriter(stringBuilder);
            options.WriteOptionDescriptions(stringWriter);
            string[] helpLines = stringBuilder.ToString().Split("\r\n");
            foreach (var line in helpLines)
            {
                _logger.Information(line);
            }
            return;
        }

        /// <summary>
        /// Initialize logging.
        /// </summary>
        private static void InitLogging()
        {
            var _loggerConfiguration = new LoggerConfiguration();

            // set the log level
            switch (_logLevel)
            {
                case "fatal":
                    _loggerConfiguration.MinimumLevel.Fatal();
                    OpcTraceToLoggerFatal = 0;
                    break;
                case "error":
                    _loggerConfiguration.MinimumLevel.Error();
                    OpcStackTraceMask = OpcTraceToLoggerError = Utils.TraceMasks.Error;
                    break;
                case "warn":
                    _loggerConfiguration.MinimumLevel.Warning();
                    OpcTraceToLoggerWarning = 0;
                    break;
                case "info":
                    _loggerConfiguration.MinimumLevel.Information();
                    OpcStackTraceMask = OpcTraceToLoggerInformation = 0;
                    break;
                case "debug":
                    _loggerConfiguration.MinimumLevel.Debug();
                    OpcStackTraceMask = OpcTraceToLoggerDebug = Utils.TraceMasks.StackTrace | Utils.TraceMasks.Operation |
                        Utils.TraceMasks.StartStop | Utils.TraceMasks.ExternalSystem | Utils.TraceMasks.Security;
                    break;
                case "verbose":
                    _loggerConfiguration.MinimumLevel.Verbose();
                    OpcStackTraceMask = OpcTraceToLoggerVerbose = Utils.TraceMasks.All;
                    break;
            }

            // set logging sinks
            _loggerConfiguration.WriteTo.Console();

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_GW_LOGP")))
            {
                _logFileName = Environment.GetEnvironmentVariable("_GW_LOGP");
            }

            if (!string.IsNullOrEmpty(_logFileName))
            {
                // configure rolling file sink
                const int MAX_LOGFILE_SIZE = 1024 * 1024;
                const int MAX_RETAINED_LOGFILES = 2;
                _loggerConfiguration.WriteTo.File(_logFileName, fileSizeLimitBytes: MAX_LOGFILE_SIZE, rollOnFileSizeLimit: true, retainedFileCountLimit: MAX_RETAINED_LOGFILES);
            }

            _logger = _loggerConfiguration.CreateLogger();
            _logger.Information($"Current directory is: {Directory.GetCurrentDirectory()}");
            _logger.Information($"Log file is: {Utils.GetAbsoluteFilePath(_logFileName, true, false, false, true)}");
            _logger.Information($"Log level is: {_logLevel}");
            return;
        }

        /// <summary>
        /// Helper to build a list of strings out of a comma separated list of strings (optional in double quotes).
        /// </summary>
        public static List<string> ParseListOfStrings(string s)
        {
            List<string> strings = new List<string>();
            if (s[0] == '"' && (s.Count(c => c.Equals('"')) % 2 == 0))
            {
                while (s.Contains('"', StringComparison.InvariantCulture))
                {
                    int first = 0;
                    int next = 0;
                    first = s.IndexOf('"', next);
                    next = s.IndexOf('"', ++first);
                    strings.Add(s[first..next]);
                    s = s.Substring(++next);
                }
            }
            else if (s.Contains(',', StringComparison.InvariantCulture))
            {
                strings = s.Split(',').ToList();
                strings = strings.Select(st => st.Trim()).ToList();
            }
            else
            {
                strings.Add(s);
            }
            return strings;
        }

        /// <summary>
        /// Helper to build a list of filenames out of a comma separated list of filenames (optional in double quotes).
        /// </summary>
        private static List<string> ParseListOfFileNames(string s, string option)
        {
            List<string> fileNames = new List<string>();
            if (s[0] == '"' && (s.Count(c => c.Equals('"')) % 2 == 0))
            {
                while (s.Contains('"', StringComparison.InvariantCulture))
                {
                    int first = 0;
                    int next = 0;
                    first = s.IndexOf('"', next);
                    next = s.IndexOf('"', ++first);
                    var fileName = s[first..next];
                    if (File.Exists(fileName))
                    {
                        fileNames.Add(fileName);
                    }
                    else
                    {
                        throw new OptionException($"The file '{fileName}' does not exist.", option);
                    }
                    s = s.Substring(++next);
                }
            }
            else if (s.Contains(',', StringComparison.InvariantCulture))
            {
                List<string> parsedFileNames = s.Split(',').ToList();
                parsedFileNames = parsedFileNames.Select(st => st.Trim()).ToList();
                foreach (var fileName in parsedFileNames)
                {
                    if (File.Exists(fileName))
                    {
                        fileNames.Add(fileName);
                    }
                    else
                    {
                        throw new OptionException($"The file '{fileName}' does not exist.", option);
                    }
                }
            }
            else
            {
                if (File.Exists(s))
                {
                    fileNames.Add(s);
                }
                else
                {
                    throw new OptionException($"The file '{s}' does not exist.", option);
                }
            }
            return fileNames;
        }

        private const int PRODUCTION_SLOT_TIME = 10000;

        private static Timer _timer = null;
        private static CancellationTokenSource _shutdownSource = null;
        private static CancellationToken _shutdownToken;
        private static ulong _productionSlot = 0;
        private static string _logFileName = $"{Utils.GetHostName()}-mes.log";
        private static string _logLevel = "info";
        private static Logger _logger;
        private static int _daysPerWeek = 0;
        private static int _firstShiftStartTime = 0600;
        private static int _shiftLengthInMinutes = 8 * 60;
        private static int _shiftCount = 3;
        private static double _shiftShouldStartLimitPercent = 0.9;
        private static bool _shiftEnabled = false;
    }
}
