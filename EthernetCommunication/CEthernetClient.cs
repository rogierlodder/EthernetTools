using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using RLStateMachine;

namespace EthernetCommunication
{
    public class CEthernetClient : CEthernetDevice
    {

        private Stopwatch receiveTimer  = new Stopwatch();
        private Stopwatch ConnectionTimer = new Stopwatch();
        private Stopwatch taskTimer = new Stopwatch();
        private bool StartConnection = false;
        private Timer CycleTimer;
        private RLSM SM = new RLSM("EthernetClientSM");

        protected Socket ServerSocket { get; set; }

        public Action<int> ByteDataReceived { get; set; }
        public Action<State> ConnectionChanged { get; set; }
        public Action TimerTick { get; set; }

        public bool Received { get; protected set; }
        public bool RCVTimeout { get; protected set; }
        public bool ConnectHasTimedOut { get; protected set; }

        public int Cycletime { get; set; } = 100;
        public int nrReceivedBytes { get; private set; } = 0;

        public State ConnState {  get { return (State)SM.CurrentState; } }

        //timeouts
        public int ConnectionTimeout { get; protected set; } = 3000;
        public int SendTimeout { get; protected set; } = 3000;
        public int ReceiveTimeout { get; protected set; } = 3000;
        public bool DisconnectReceived { get; private set; } = false;
        public bool SendTimedOut { get; private set; }
        public bool SocketCreated { get; private set; }

        public CEthernetClient(string name)
        {
            ConnectionName = name;
            CycleTimer = new Timer(RunFromLocalTimer);

            SM.StateChanged = p => ConnectionChanged((State)p);

            SM.AddState(State.NotConnected, new List<Transition>
            {
                new Transition("StartConnection", () => StartConnection, () => StartConn(), State.StartingConnection)
            }, () =>
            {
                SocketCreated = false;
                SendTimedOut = false;
                DisconnectReceived = false;
            }, StateType.entry);

            SM.AddState(State.StartingConnection, new List<Transition>
            {
                new Transition("ConnectionStarted", () => SocketCreated, null, State.Connecting),
            }, null, StateType.transition);

            SM.AddState(State.Connecting, new List<Transition>
            {
                new Transition("ConnectionReceived", () => ServerSocket.Connected, () =>
                {
                    Connstats.NrConnects++;
                    Connstats.ConnectionTime.Reset();
                    ConnectHasTimedOut = false;
                }, State.Connected),
                new Transition("ConnectionTimeout", () => ConnectionTimer.ElapsedMilliseconds > ConnectionTimeout, ()=>
                {
                    ConnectHasTimedOut = true;
                }, State.NotConnected)
            }, null, StateType.transition);

            SM.AddState(State.Connected, new List<Transition>
            {
                //new Transition("NotConnected", () => ServerSocket.Connected == false, () => Connstats.NrDisconnects++, State.NotConnected),
                new Transition("SendTimeout", () => SendTimedOut, () => Connstats.NrDisconnects++, State.NotConnected),
                new Transition("Disconnect", () => StartConnection == false || DisconnectReceived, () => { }, State.NotConnected),
                new Transition("ReceiveTimeout", () => receiveTimer.ElapsedMilliseconds > ReceiveTimeout, () =>
                {
                    receiveTimer.Reset();
                    RCVTimeout = true;
                    Received = false;
                    Connstats.NrDisconnects++;
                }, State.NotConnected),
            }, () => 
            {
                if (Received)
                {
                    receiveTimer.Stop();
                }
            }, StateType.idle);

            SM.SaveGraph(@"C:\temp\CethernetClient");
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
            SM.Reset();
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
            if ((State)SM.CurrentState != State.Connected) return false;
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
                SendTimedOut = true; //when the send fails, the receive timout can be called immediately
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
            receiveTimer.Restart();
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
                DisconnectReceived = true;
            }
        }

        /// <summary>
        /// Start and run the connection with the local timer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RunFromLocalTimer(object sender)
        {
            TimerTick?.Invoke();
            Run();
        }

        /// <summary>
        /// The state machine of the connection. Requires an external call (most likely from a Timer) to run.
        /// </summary>
        public void Run()
        {
            SM.Run();
        }

        private void StartConn()
        {
            if (ServerSocket != null)
            {
                ServerSocket.Close();
                ServerSocket.Dispose();
            }
            ServerSocket = new Socket(AddressFamily.InterNetwork, SockType, ProtType);
            ServerSocket.Blocking = false;

            ConnectionTimer.Restart();
            receiveTimer.Restart();

            SocketCreated = true;

            //completely suppress the socket exception. There will always be an exception since the socket was set to non-blocking
            try
            {
                ServerSocket.Connect(IPaddress, Port);
            }
            catch { }
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
