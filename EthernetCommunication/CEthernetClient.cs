using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;

namespace EthernetCommunication
{
    public class CEthernetClient : CEthernetDevice
    {
        public Action<int> ByteDataReceived { get; set; }
        public Action<string, State> ConnectionChanged { get; set; }

        private Stopwatch receiveTimer  = new Stopwatch();
        private Stopwatch ConnectionTimer = new Stopwatch();
        private Stopwatch taskTimer = new Stopwatch();
        private bool StartConnection = false;
        private State _ConnState = State.NotConnected;

        private Timer CycleTimer;

        public bool Received { get; protected set; }
        public bool RCVTimeout { get; protected set; }
        public bool ConnectHasTimedOut { get; protected set; }

        public int Cycletime { get; set; } = 100;
        public int nrReceivedBytes { get; private set; } = 0;

        public State ConnState
        {
            get { return _ConnState; }
            set
            {
                _ConnState = value;
                ConnectionChanged?.Invoke(ConnectionName, _ConnState);
            }
        }

        protected Socket ServerSocket { get; set; }

        //timeouts
        public int ConnectionTimeout { get; protected set; } = 3000;
        public int SendTimeout { get; protected set; } = 3000;
        public int ReceiveTimeout { get; protected set; } = 30000;

        public CEthernetClient(string name)
        {
            ConnectionName = name;
            CycleTimer = new Timer(RunFromLocalTimer);
        }

        /// <summary>
        /// Change the connection parameters
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <param name="socktype"></param>
        public void SetConnection(string ip, int port, string socktype)
        {
            IPaddress = IPAddress.Parse(ip);
            Port = port;

            if (socktype == "UDP")
            {
                SockType = SocketType.Dgram;
                ProtType = ProtocolType.Udp;
            }
            if (socktype == "TCP")
            {
                SockType = SocketType.Stream;
                ProtType = ProtocolType.Tcp;
            }

        }

        public void Connect()
        {
            StartConnection = true;
        }

        public void Disconnect()
        {
            ConnState = State.NotConnected;
            StartConnection = false;
            CycleTimer.Change(0, Timeout.Infinite);

            if (ServerSocket != null && ServerSocket.Connected == true)
            {
                try
                {
                    ServerSocket.Disconnect(false);
                }
                catch { }
            }
        }

        public void ConnectAndStart()
        {
            StartConnection = true;
            CycleTimer.Change(0, Cycletime);
        }

        /// <summary>
        /// Send a command and start the asynchronous wait for the reply
        /// </summary>
        /// <param name="DP">Dataprocessing object to be used</param>
        /// <returns></returns>
        public bool SendData(byte[] sendBuf, int nrBytesToSend, byte[] receiveBuf)
        {
            Received = false;
            if (_ConnState != State.Connected) return false;
            try
            {
                RCVTimeout = false;

                receiveTimer.Restart();

                ServerSocket.BeginSend(sendBuf, 0, nrBytesToSend, SocketFlags.None, new AsyncCallback(SendCallback), ServerSocket);
                ServerSocket.BeginReceive(receiveBuf, 0, receiveBuf.Length, 0, new AsyncCallback(ReceiveCallback), ServerSocket);
                return true;
            }
            catch
            {
                RCVTimeout = true; //when the send fails, the receive timout can be called immediately
                _ConnState = State.NotConnected;
                Connstats.NrDisconnects++;
                return false;
             }
        }

        /// <summary>
        /// The SendCallBack that is called when data is successfully sent
        /// </summary>
        /// <param name="ar"></param>
        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                Socket clnt = (Socket)ar.AsyncState;
                int bytesSent = clnt.EndSend(ar);
                Connstats.Sentpackets++;
            }
            catch { }
        }

        /// <summary>
        /// The receive call back from the BeginReceive command issued when a command is sent
        /// </summary>
        /// <param name="ar"></param>
        private void ReceiveCallback(IAsyncResult ar)
        {
            //reset nr of received bytes value
            nrReceivedBytes = 0;

            Socket clnt = (Socket)ar.AsyncState;
            try
            {
                nrReceivedBytes = clnt.EndReceive(ar);
            }
            catch
            {

            }
            if (nrReceivedBytes > 0)
            {
                //Signal the arrival of new data with the delegate
                ByteDataReceived?.Invoke(nrReceivedBytes);

                Connstats.Receivedpackets++;
                Received = true;
                RCVTimeout = false;
            }
            if (nrReceivedBytes == 0) //a disconnect was received
            {
                _ConnState = State.NotConnected;
            }
        }

        /// <summary>
        /// Start and run the connection with the local timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RunFromLocalTimer(object sender)
        {
            StartConnection = true;
            Run();
        }

        /// <summary>
        /// The state machine of the connection. Requires an external call (most likely from a Timer) to run.
        /// </summary>
        public void Run()
        {
            if (ConnState == State.NotConnected)
            {
                ConnectHasTimedOut = false;
                if (StartConnection)
                {
                    _ConnState = State.StartingConnection;
                }
            }

            if (ConnState == State.StartingConnection)
            {
                if (ServerSocket != null)
                {
                    ServerSocket.Close();
                    ServerSocket.Dispose();
                }
                ServerSocket = new Socket(AddressFamily.InterNetwork, SockType, ProtType);
                ServerSocket.Blocking = false;

                //completely suppress the socket exception. There will always be an exception since the socket was set to non-blocking
                try
                {
                    ConnState = State.Connecting;
                    ConnectionTimer.Restart();
                    ServerSocket.Connect(IPaddress, Port);
                }
                catch { }
            }
               
            if (ConnState == State.Connecting)
            {
                if (ServerSocket.Connected == true)
                {
                    ConnState = State.Connected;
                    Connstats.NrConnects++;
                    Connstats.ConnectionTime.Reset();
                    ConnectHasTimedOut = false;
                }
                else
                {
                    if (ConnectionTimer.ElapsedMilliseconds > ConnectionTimeout)
                    {
                        ConnectHasTimedOut = true;
                        _ConnState = State.StartingConnection;
                    }
                }
            } 

            if (ConnState == State.Connected)
            {
                if (ServerSocket.Connected == false)
                {
                    _ConnState = State.NotConnected;
                    Connstats.NrDisconnects++;
                }

                if (receiveTimer.ElapsedMilliseconds > ReceiveTimeout)
                {
                    receiveTimer.Reset();
                    RCVTimeout = true;
                    Received = false;
                    _ConnState = State.StartingConnection;
                    Connstats.NrDisconnects++;
                }

                if (Received)
                {
                    receiveTimer.Stop();
                }
            }
        }

        /// <summary>
        /// Task for waiting on the reply of the sent command
        /// </summary>
        /// <returns></returns>
        public Task<bool> ReplyTask()
        {
            return Task.Run(() =>
            {
                while (RCVTimeout == false && Received == false)
                {
                    Thread.Sleep(10);
                }
                if (RCVTimeout == false && Received == true) return true; else return false;
            });
        }

        /// <summary> 
        /// Task for waiting on the connection to be established
        /// </summary>
        public Task ConnectedTask()
        {
            return Task.Run(() =>
            {
                while ( ConnState != State.Connected )
                {
                    Thread.Sleep(10);
                }
            });
        }


    }
}
