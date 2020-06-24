
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CfMes
{
    using Opc.Ua;
    using Serilog;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using static OpcApplicationConfiguration;
    using static Program;

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

        public ulong ProductSerialNumber { get; set; }

        public bool IsReady => Status == StationStatus.Ready;

        public bool IsFault => Status == StationStatus.Fault;

        public bool IsDone => Status == StationStatus.Done;

        public bool IsInProgress => Status == StationStatus.WorkInProgress;

        public bool IsDisconnected => _reconnectHandler != null;

        public Station(string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken ct)
        {
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
                Logger.Fatal(e, "The endpoint URL '{_endpointUrl}' has an invalid format!");
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
                    Logger.Fatal($"Failed to create session to endpoint at {EndpointUrl}! Wait {RECONNECT_DELAY} seconds and retry...");
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
                        Logger.Error($"Failed to create monitored item for station status at {EndpointUrl}! Wait {RECONNECT_DELAY} seconds and retry...");
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
                Logger.Information($"Create session to endpoint {EndpointUrl}.");
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
                Logger.Fatal(e, $"Failed to create session to endpoint {EndpointUrl}!");
            }
            if (_session != null)
            {
                _session.KeepAlive += new KeepAliveEventHandler((sender, e) => Client_KeepAlive(sender, e));
            }
            else
            {
                Logger.Error($"Could not create session to endpoint {EndpointUrl}!");
                return false;
            }
            Logger.Information($"Session to endpoint {EndpointUrl} established.");
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
                    Logger.Information($"Start monitoring status node on endpoint {EndpointUrl}.");
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
                        Logger.Information($"Now monitoring status node on endpoint {EndpointUrl}.");
                        return true;
                    }
                    else
                    {
                        Logger.Error("Could not create subscription to monitor station status!");
                    }
                }
                catch (Exception e)
                {
                    Logger.Fatal(e, "Could not create subscription to monitor station status!");
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
                Logger.Warning($"Endpoint: {EndpointUrl} Status: {e.Status}, Outstanding requests: {sender.OutstandingRequestCount},  Defunct requests: {sender.DefunctRequestCount}");

                if (_reconnectHandler == null)
                {
                    Logger.Information($"--- RECONNECTING to endpoint {EndpointUrl}  ---");
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

            Logger.Information($"--- RECONNECTED to endpoint {EndpointUrl}  ---");
        }

        /// <summary>
        /// Calls the Execute method in the station.
        /// </summary>
        public void Execute()
        {
            bool callSuccessfull = false;
            int retryCount = 1;

            VariantCollection inputArgumentsProductSerialNumber = new VariantCollection()
            {
                ProductSerialNumber
            };

            while (!callSuccessfull && !_shutdownToken.IsCancellationRequested)
            {
                try
                {
                    if (_reconnectHandler != null)
                    {
                        Logger.Debug($"In reconnect. Wait {RECONNECT_PERIOD} msec till retry calling Execute method.");
                        Task.Delay(RECONNECT_PERIOD, _shutdownToken).Wait();
                        continue;
                    }
                    if (retryCount++ > 1)
                    {
                        Logger.Warning($"Retry {retryCount}th time to call Execute method on endpoint {EndpointUrl}.");
                    }
                    CallMethodRequestCollection requests = new CallMethodRequestCollection();
                    CallMethodRequest request = new CallMethodRequest
                    {
                        ObjectId = new NodeId("Methods", 2),
                        MethodId = new NodeId("Execute", 2),
                    };
                    request.InputArguments = inputArgumentsProductSerialNumber;
                    requests.Add(request);
                    ResponseHeader responseHeader = _session.Call(null, requests, out CallMethodResultCollection results, out DiagnosticInfoCollection diagnosticInfos);
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        Logger.Error($"Execute call was not successfull on endpoint URL {EndpointUrl} (status: '{results[0].StatusCode}'. Retry...");
                    }
                    else
                    {
                        callSuccessfull = true;
                    }
                }
                catch (Exception e)
                {
                    Logger.Fatal($"Exception when calling Execute method on endpoint URL {EndpointUrl}. Retry...");
                    Logger.Fatal(e, "Exception details:");
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
                        Logger.Debug($"In reconnect. Wait {RECONNECT_PERIOD} msec till retry calling OpenPressureReleaseValve method.");
                        Task.Delay(RECONNECT_PERIOD, _shutdownToken).Wait();
                        continue;
                    }
                    if (retryCount++ > 1)
                    {
                        Logger.Warning($"Retry {retryCount}th time to call OpenPressureReleaseValve method on endpoint {EndpointUrl}.");
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
                        Logger.Error($"OpenPressureReleaseValve call was not successfull on endpoint URL {EndpointUrl} (status: '{results[0].StatusCode}'");
                    }
                    else
                    {
                        callSuccessfull = true;
                    }
                }
                catch (Exception e)
                {
                    Logger.Fatal($"Exception when calling OpenPressureReleaseValve method on endpoint URL {EndpointUrl}");
                    Logger.Fatal(e, "Exception details:");
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
                        Logger.Debug($"In reconnect. Wait {RECONNECT_PERIOD} msec till retry calling Reset method.");
                        Task.Delay(RECONNECT_PERIOD, _shutdownToken).Wait();
                        continue;
                    }
                    if (retryCount++ > 1)
                    {
                        Logger.Warning($"Retry {retryCount}th time to call Reset method on endpoint {EndpointUrl}.");
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
                        Logger.Error($"Reset call was not successfull on endpoint URL {EndpointUrl} (status: '{results[0].StatusCode}'");
                    }
                    else
                    {
                        callSuccessfull = true;
                    }
                }
                catch (Exception e)
                {
                    Logger.Fatal($"Exception when calling Reset method on endpoint URL {EndpointUrl}");
                    Logger.Fatal(e, "Exception details:");
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
        public AssemblyStation(string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken shutdownToken) : base(endpointUrl, mesConfiguration, shutdownToken)
        {
            Logger.Information($"AssemblyStation URL is: {endpointUrl}");
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

                Logger.Debug($"AssemblyStation: status changed to {Status}");

                // now check what the status is
                switch (Status)
                {
                    case StationStatus.Ready:
                        // build the next product by calling execute with new serial number
                        ProductSerialNumber++;
                        Execute();
                        Logger.Information($"AssemblyStation: now building #{ProductSerialNumber}");
                        break;

                    case StationStatus.Discarded:
                        // product was automatically discarded by the station, reset
                        Logger.Debug($"AssemblyStation: #{ProductSerialNumber} discarded");
                        Reset();
                        break;

                    case StationStatus.Fault:
                        Task.Run(async () =>
                        {
                            // station is at fault state, wait some time to simulate manual intervention before reseting
                            Logger.Information("AssemblyStation: <<Fault detected>>");
                            await Task.Delay(FAULT_DELAY).ConfigureAwait(false);
                            Logger.Information("AssemblyStation: <<Fix Fault>>");
                            Reset();
                        });
                        break;

                    case StationStatus.WorkInProgress:
                    case StationStatus.Done:
                        break;

                    default:
                        Logger.Error("Argument error: Invalid station status type received!");
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Error processing monitored item notification in AssemblyStation");
            }
            finally
            {
                StationControl.Release();
            }
        }
    }

    public class TestStation : Station
    {
        public TestStation(string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken shutdownToken) : base(endpointUrl, mesConfiguration, shutdownToken)
        {
            Logger.Information($"TestStation URL is: {endpointUrl}");
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

                Logger.Debug($"TestStation: status changed to {Status}");

                switch (Status)
                {
                    case StationStatus.Done:
                        Logger.Debug($"TestStation: #{ProductSerialNumber} testing passed");
                        break;

                    case StationStatus.Discarded:
                        Logger.Debug($"TestStation:  #{ProductSerialNumber} testing failed -> discard");
                        Reset();
                        break;

                    case StationStatus.Fault:
                        {
                            Task.Run(async () =>
                            {
                                Logger.Information("TestStation: <<Fault detected>>");
                                await Task.Delay(FAULT_DELAY).ConfigureAwait(false);
                                Logger.Information("TestStation: <<Fix Fault>>");
                                Reset();
                            });
                        }
                        break;

                    case StationStatus.Ready:
                    case StationStatus.WorkInProgress:
                        break;

                    default:
                        {
                            Logger.Error("Argument error: Invalid station status type received!");
                            return;
                        }
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Exception: Error processing monitored item notification in TestStation");
            }
            finally
            {
                StationControl.Release();
            }
        }
    }

    public class PackagingStation : Station
    {
        public PackagingStation(string endpointUrl, ApplicationConfiguration mesConfiguration, CancellationToken shutdownToken) : base(endpointUrl, mesConfiguration, shutdownToken)
        {
            Logger.Information($"PackagingStation URL is: {endpointUrl}");
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

                    Logger.Debug($"PackagingStation: status changed to {Status}");

                    switch (Status)
                    {
                        case StationStatus.Ready:
                        case StationStatus.WorkInProgress:
                            break;

                        case StationStatus.Done:
                        Logger.Information($"PackagingStation: #{ProductSerialNumber} completed successfully");
                            Reset();
                            break;

                        case StationStatus.Discarded:
                        Logger.Information($"PackagingStation: #{ProductSerialNumber} completed, but not good");
                            Reset();
                            break;

                        case StationStatus.Fault:
                            {
                                Task.Run(async () =>
                                {
                                    Logger.Information("PackagingStation: <<Fault detected>>");
                                    await Task.Delay(FAULT_DELAY).ConfigureAwait(false);
                                    Logger.Information("PackagingStation: <<Fix Fault>>");
                                    Reset();
                                });
                            }
                            break;

                        default:
                            Logger.Error("Argument error: Invalid station status type received!");
                            break;
                    }
                }
            catch (Exception e)
            {
                Logger.Fatal(e, "Exception: Error processing monitored item notification in PackagingStation");
            }
            finally
            {
                StationControl.Release();
            }
        }
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

        public static Serilog.Core.Logger Logger = null;

        /// <summary>
        /// Synchronous main method of the app.
        /// </summary>
        public static void Main(string[] args)
        {
            // when debugging with VS Code, this loop could be enabled to break at startup
            //bool wait = true;
            //while (wait)
            //{
            //    Thread.Sleep(10000);
            //}

            MainAsync(args).Wait();
        }

        /// <summary>
        /// Asynchronous part of the main method of the app.
        /// </summary>
        public static async Task MainAsync(string[] args)
        {
            _shutdownSource = new CancellationTokenSource();
            _shutdownToken = _shutdownSource.Token;
            var quitEvent = new ManualResetEvent(false);
            var shouldShowHelp = false;

            // command line options
            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                // endpoint configuration options
                { "as|assemblystation=", $"the endpoint of the assemblystation.\nDefault: '{AssemblyStationEndpointUrl}'", (string s) => AssemblyStationEndpointUrl = s },
                { "ts|teststation=", $"the endpoint of the teststation.\nDefault: '{TestStationEndpointUrl}'", (string s) => TestStationEndpointUrl = s },
                { "ps|packagingstation=", $"the endpoint of the packagingstation.\nDefault: '{PackagingStationEndpointUrl}'", (string s) => PackagingStationEndpointUrl = s },

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
                            throw new Mono.Options.OptionException("The loglevel must be one of: fatal, error, warn, info, debug, verbose", "loglevel");
                        }
                    }
                },
                { "aa|autoaccept", $"auto accept station server certificates\nDefault: '{AutoAcceptCerts}'", a => AutoAcceptCerts = a != null },
                { "to|trustowncert", $"the cfmes certificate is put into the trusted certificate store automatically.\nDefault: {TrustMyself}", t => TrustMyself = t != null },

                { "ap|appcertstorepath=", $"the path where the own application cert should be stored\nDefault :'{OpcOwnCertStorePath}'", (string s) => OpcOwnCertStorePath = s
                },

                { "tp|trustedcertstorepath=", $"the path of the trusted cert store\nDefault '{OpcTrustedCertStorePath}'", (string s) => OpcTrustedCertStorePath = s
                },

                { "rp|rejectedcertstorepath=", $"the path of the rejected cert store\nDefault '{OpcRejectedCertStorePath}'", (string s) => OpcRejectedCertStorePath = s
                },

                { "ip|issuercertstorepath=", $"the path of the trusted issuer cert store\nDefault '{OpcIssuerCertStorePath}'", (string s) => OpcIssuerCertStorePath = s
                },

                // misc
                { "h|help", "show this message and exit", h => shouldShowHelp = h != null },
            };

            List<string> extraArgs = new List<string>();
            try
            {
                // parse the command line
                extraArgs = options.Parse(args);
            }
            catch (Mono.Options.OptionException e)
            {
                // initialize logging
                InitLogging();

                // show message
                Logger.Fatal(e, "Error in command line options");

                // show usage
                Usage(options);
                return;
            }

            // initialize logging
            InitLogging();

            // check args
            if (extraArgs.Count != 0 || shouldShowHelp)
            {
                // show usage
                Usage(options);
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
                Logger.Information("Connectedfactory Manufacturing Execution System starting up. Press CTRL-C to exit.");

                // create stations
                OpcApplicationConfiguration mesOpcConfiguration = new OpcApplicationConfiguration();
                ApplicationConfiguration mesConfiguration = await mesOpcConfiguration.ConfigureAsync().ConfigureAwait(false);
                AssemblyStation = new AssemblyStation(AssemblyStationEndpointUrl, mesConfiguration, _shutdownToken);
                TestStation = new TestStation(TestStationEndpointUrl, mesConfiguration, _shutdownToken);
                PackagingStation = new PackagingStation(PackagingStationEndpointUrl, mesConfiguration, _shutdownToken);

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
                    // continue production with the last product serial number
                    AssemblyStation.ProductSerialNumber = Math.Max(AssemblyStation.ProductSerialNumber, Math.Max(TestStation.ProductSerialNumber, PackagingStation.ProductSerialNumber)) + 1;
                    Logger.Information($"MES: Start production line by assembling product with product serial number #{AssemblyStation.ProductSerialNumber}");
                    await StationControl.WaitAsync().ConfigureAwait(false);
                    AssemblyStation.Execute();
                    StartProductionSlot();
                    StationControl.Release();
                }

                // wait for Ctrl-C
                quitEvent.WaitOne(Timeout.Infinite);
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "MES failed unexpectedly!");
                StationControl = null;
                StationControl.Dispose();
            }
            Logger.Information("MES is exiting...");
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
                    Logger.Debug($"MES: moving #{AssemblyStation.ProductSerialNumber} from AssemblyStation to TestStation");
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
                    Logger.Debug($"MES: moving #{TestStation.ProductSerialNumber} from TestStation to PackagingStation");
                    PackagingStation.ProductSerialNumber = TestStation.ProductSerialNumber;
                    PackagingStation.Execute();
                    TestStation.Reset();
                }
            }
            catch (Exception e)
            {
                Logger.Fatal(e, "Error in MES logic!");
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
        private static void StartProductionSlot()
        {
            // defer if there is any fault or any reconnect in progress
            while (true)
            {
                if (AssemblyStation.IsDisconnected || TestStation.IsDisconnected || PackagingStation.IsDisconnected)
                {
                    Task.Delay(PRODUCTION_SLOT_TIME, _shutdownToken).Wait();
                    continue;
                }
                if (AssemblyStation.IsFault || TestStation.IsFault || PackagingStation.IsFault)
                {
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

            Logger.Debug($"MES: Starting production slot {_productionSlot++}");
            _timer = new Timer(MesLogic, null, PRODUCTION_SLOT_TIME, Timeout.Infinite);
        }

        /// <summary>
        /// Usage message.
        /// </summary>
        private static void Usage(Mono.Options.OptionSet options)
        {
            // show some app description message
            Logger.Information($"Usage: {Assembly.GetEntryAssembly().GetName().Name}.exe [<options>]");
            Logger.Information("");

            // output the options
            Logger.Information("Options:");
            StringBuilder stringBuilder = new StringBuilder();
            StringWriter stringWriter = new StringWriter(stringBuilder);
            options.WriteOptionDescriptions(stringWriter);
            string[] helpLines = stringBuilder.ToString().Split("\r\n");
            foreach (var line in helpLines)
            {
                Logger.Information(line);
            }
            return;
        }

        /// <summary>
        /// Initialize logging.
        /// </summary>
        private static void InitLogging()
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration();

            // set the log level
            switch (_logLevel)
            {
                case "fatal":
                    loggerConfiguration.MinimumLevel.Fatal();
                    OpcTraceToLoggerFatal = 0;
                    break;
                case "error":
                    loggerConfiguration.MinimumLevel.Error();
                    OpcStackTraceMask = OpcTraceToLoggerError = Utils.TraceMasks.Error;
                    break;
                case "warn":
                    loggerConfiguration.MinimumLevel.Warning();
                    OpcTraceToLoggerWarning = 0;
                    break;
                case "info":
                    loggerConfiguration.MinimumLevel.Information();
                    OpcStackTraceMask = OpcTraceToLoggerInformation = 0;
                    break;
                case "debug":
                    loggerConfiguration.MinimumLevel.Debug();
                    OpcStackTraceMask = OpcTraceToLoggerDebug = Utils.TraceMasks.StackTrace | Utils.TraceMasks.Operation |
                        Utils.TraceMasks.StartStop | Utils.TraceMasks.ExternalSystem | Utils.TraceMasks.Security;
                    break;
                case "verbose":
                    loggerConfiguration.MinimumLevel.Verbose();
                    OpcStackTraceMask = OpcTraceToLoggerVerbose = Utils.TraceMasks.All;
                    break;
            }

            // set logging sinks
            loggerConfiguration.WriteTo.Console();

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_GW_LOGP")))
            {
                _logFileName = Environment.GetEnvironmentVariable("_GW_LOGP");
            }

            if (!string.IsNullOrEmpty(_logFileName))
            {
                // configure rolling file sink
                const int MAX_LOGFILE_SIZE = 1024 * 1024;
                const int MAX_RETAINED_LOGFILES = 2;
                loggerConfiguration.WriteTo.File(_logFileName, fileSizeLimitBytes: MAX_LOGFILE_SIZE, rollOnFileSizeLimit: true, retainedFileCountLimit: MAX_RETAINED_LOGFILES);
            }

            Logger = loggerConfiguration.CreateLogger();
            Logger.Information($"Current directory is: {Directory.GetCurrentDirectory()}");
            Logger.Information($"Log file is: {Utils.GetAbsoluteFilePath(_logFileName, true, false, false, true)}");
            Logger.Information($"Log level is: {_logLevel}");
            return;
        }

        private const int PRODUCTION_SLOT_TIME = 1000;

        private static Timer _timer = null;
        private static CancellationTokenSource _shutdownSource = null;
        private static CancellationToken _shutdownToken;
        private static ulong _productionSlot = 0;
        private static string _logFileName = $"{Utils.GetHostName()}-mes.log";
        private static string _logLevel = "info";
    }
}
