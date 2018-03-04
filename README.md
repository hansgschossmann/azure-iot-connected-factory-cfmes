# azure-iot-connected-factory-cfmes

CfMes is an OPC UA client and implements a simple Manufacturing Exection System of a production line for the factory simulation in [Connectedfactory](https://github.com/Azure/azure-iot-connected-factory).
The MES controls three stations (Assembly, Test, Packaging) of the production line. To connect to these stations, the OPC UA endpoint URLs must be passed in via command line parameters.
If they are ommited, then the MES expects the stations on different ports on the same host as the MES is running (Assembly: 51210, Test: 51211, Packaging: 51212).

The implementation of the station can be found [here](https://github.com/hansgschossmann/azure-iot-connected-factory-cfstation).

A docker container of this repository is available [here](https://hub.docker.com/r/hansgschossmann/azure-iot-connected-factory-cfmes).

The command line usage is:

     Usage: CfMes.exe [<options>]

     Options:
           --as, --assemblystation=VALUE
                                  the endpoint of the assemblystation.
                                    Default: 'opc.tcp://johanngnb:51210'
           --ts, --teststation=VALUE
                                  the endpoint of the teststation.
                                    Default: 'opc.tcp://johanngnb:51211'
           --ps, --packagingstation=VALUE
                                  the endpoint of the packagingstation.
                                    Default: 'opc.tcp://johanngnb:51212'
           --lf, --logfile=VALUE  the filename of the logfile to use.
                                    Default: 'johanngnb-MES.log'
           --aa, --autoaccept     auto accept station server certificates
                                    Default: '_autoAcceptCerts'
           --st, --opcstacktracemask=VALUE
                                  the trace mask for the OPC stack. See github OPC .
                                    NET stack for definitions.

                                    Default: 285  (645)
       -h, --help                 show this message and exit
