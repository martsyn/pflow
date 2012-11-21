using System;
using System.Net.Sockets;
using System.Threading;

namespace pflow
{
    internal class Forwarder
    {
        private readonly Socket _srcSocket;
        private readonly string _srcName;
        private readonly string _dstName;
        private readonly Func<Socket> _dstSocketFunc;

        public Forwarder(Socket srcSocket, string srcName, Socket dstSocket, string dstName)
            : this(srcSocket, srcName, () => dstSocket, dstName)
        {
        }

        public Forwarder(Socket srcSocket, string srcName, Func<Socket> dstSocketFunc, string dstName)
        {
            _srcSocket = srcSocket;
            _srcName = srcName;
            _dstSocketFunc = dstSocketFunc;
            _dstName = dstName;
        }

        public void StartInNewThread()
        {
            new Thread(RunInCurrentThread).Start();
        }

        public void RunInCurrentThread()
        {
            Socket dstSocket = null;
            var srcDescription = string.Format("{0} - {1}", _srcName, _srcSocket.RemoteEndPoint);
            string dstDescription = null;
            var buf = new byte[16384];
            
            for (; ; )
            {
                int len;
                try
                {
                    len = _srcSocket.Receive(buf);
                }
                catch (Exception x)
                {
                    Console.Error.WriteLine("Disconnected: {0} - ({1}): {2}", srcDescription, x.GetType(), x.Message);
                    break;
                }

                if (len <= 0)
                {
                    Console.Error.WriteLine("Disconnected: {0}", srcDescription);
                    break;
                }

                if (dstSocket == null)
                {
                    dstSocket = _dstSocketFunc();
                    if (dstSocket == null)
                    {
                        Console.Error.WriteLine("Destination not connected, disconnecting {0} as well", srcDescription);
                        _srcSocket.Dispose();
                        return;
                    }
                    dstDescription = string.Format("{0} - {1}", _dstName, dstSocket.RemoteEndPoint);
                }

                try
                {
                    dstSocket.Send(buf, len, SocketFlags.None);
                }
                catch (Exception x)
                {
                    Console.Error.WriteLine("Disconnected: {0} - ({1}): {2}", dstDescription, x.GetType(), x.Message);
                }
            }

        }
    }
}