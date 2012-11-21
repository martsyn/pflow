using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace pflow
{
    class PflowProgram
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                ShowUsage();
                //return;
            }
            
            switch (args[0])
            {
                case "forward":
                    {
                        if (args.Length != 3)
                            goto default;

                        string receiver = args[1];
                        int rcvCol = receiver.IndexOf(':');
                        if (rcvCol < 0)
                            goto default;
                        int rcvPort;
                        if (!int.TryParse(receiver.Substring(rcvCol + 1), out rcvPort))
                            ShowUsage();
                        string rcvHost = receiver.Substring(0, rcvCol);

                        string server = args[2];
                        string srvHost;
                        int srvCol = server.IndexOf(':');
                        string srvPortStr;
                        int srvPort;
                        if (srvCol < 0)
                        {
                            srvHost = "localhost";
                            srvPortStr = server;
                        }
                        else
                        {
                            srvHost = server.Substring(0, srvCol);
                            srvPortStr = server.Substring(srvCol + 1);
                        }
                        if (!int.TryParse(srvPortStr, out srvPort))
                            ShowUsage();

                        Forward(rcvHost, rcvPort, srvHost, srvPort);
                    }
                    break;
                case "receive":
                    {
                        int dmzPort, fwdPort;
                        if (args.Length != 3
                            || !int.TryParse(args[1], out dmzPort)
                            || !int.TryParse(args[2], out fwdPort))
                            goto default;
                        Receive(dmzPort, fwdPort);
                    }
                    break;
                default:
                    ShowUsage();
                    break;
            }
        }

        private static void Forward(string rcvHost, int rcvPort, string srvHost, int srvPort)
        {
            var evt = new ManualResetEvent(false);
            for (;;)
            {
                Socket rcvSocket;
                try
                {
                    Console.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: connecting to receiver {1}:{2}", DateTime.Now, rcvHost, rcvPort);
                    rcvSocket = new TcpClient(rcvHost, rcvPort).Client;
                }
                catch (Exception x)
                {
                    Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: receiver connection failed, retrying in 20 seconds: ({1} - {2})", DateTime.Now, x.GetType().Name, x.Message);
                    Thread.Sleep(20000);
                    continue;
                }

                Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: connected to receiver {1}", DateTime.Now, rcvSocket.RemoteEndPoint);

                new Forwarder(
                    rcvSocket, "rcv",
                    () =>
                    {
                        try
                        {
                            evt.Set();
                            Console.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: connecting to server {1}:{2}", DateTime.Now, srvHost, srvPort);
                            var srvSocket = new TcpClient(srvHost, srvPort).Client;
                            Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: connected to server {1}", DateTime.Now, srvSocket.RemoteEndPoint);
                            new Forwarder(srvSocket, "srv", rcvSocket, "rcv").StartInNewThread();
                            return srvSocket;
                        }
                        catch (Exception x)
                        {
                            Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: server connection failed: ({1} - {2})", DateTime.Now, x.GetType().Name, x.Message);
                            return null;
                        }
                    }, "srv").StartInNewThread();

                evt.WaitOne();
                evt.Reset();
            }
// ReSharper disable FunctionNeverReturns
        }
// ReSharper restore FunctionNeverReturns

        private static void Receive(int dmzPort, int fwdPort)
        {
            var dmzListener = new TcpListener(IPAddress.Any, dmzPort);
            dmzListener.Start();
            TcpListener fwdListener = null;

            for (;;)
            {
                Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: waiting dmz connection, port {1}...", DateTime.Now, dmzPort);
                var dmzSocket = dmzListener.AcceptSocket();
                Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: dmz connection from {1}", DateTime.Now, dmzSocket.RemoteEndPoint);

                if (fwdListener == null)
                {
                    fwdListener = new TcpListener(IPAddress.Any, fwdPort);
                    fwdListener.Start();
                }

                Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: waiting fwd connection, port {1}...", DateTime.Now, fwdPort);
                var fwdSocket = fwdListener.AcceptSocket();

                Console.Error.WriteLine("{0:yyyy/MM/dd HH:mm:ss.fff}: fwd connection from {1}", DateTime.Now, fwdSocket.RemoteEndPoint);

                new Forwarder(dmzSocket, "dmz", fwdSocket, "fwd").StartInNewThread();
                new Forwarder(fwdSocket, "fwd", dmzSocket, "dmz").StartInNewThread();
            }
// ReSharper disable FunctionNeverReturns
        }
// ReSharper restore FunctionNeverReturns

        private static void ShowUsage()
        {
            Console.Error.WriteLine(@"
Usage:

    pflow.exe command arguments

Pflow lets you tunnel a TCP port hidden by NAT gateway, which only allows
outgoing connections (a common setup in most private networks). The tunnel is
established by connecting forwarder running inside the private network to
receiver on a host with an external (DMZ) port.

Picture:

         home         |    teh    |        office
        network       | internets |        network
          |           |           |           |
 client-->)---pflow---(<--------------pflow-->)--server
          |  receive  |           |  forward  |
          |           |           |           |
          |fwd     dmz|          NAT          |srv
          |port   port|        gateway        |port

Where:
    
    )--       - Listening TCP port.
    <--       - TCP client.
    dmzport   - Port exposed outside for forwarder to connect to.
    fwdport   - Forwarding port opened for a client program to connect to.
    srvport   - Port you want to make available outside.

Syntax:

    pflow receive dmzport fwdport

 - Listen on dmzport for forwarder. Once forwarder connects, wait for a client
connection on fwdport and forwards traffic between them.

    pflow forward receiver.hostname:dmzport [server.hostname:]srvport

 - Connect to receiver's DMZ port. If connection fails, continue retrying
every 20 seconds. If server's hostname is omitted, 'localhost' is used.
Note: the connection to server is established only when client sends first
data packet.

Examples:

    pflow receive 54321 54322
    pflow forward myself.no-ip.com:54321 3389");
        }
    }
}
