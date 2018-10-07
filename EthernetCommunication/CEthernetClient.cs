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
        //timers
        private Stopwatch receiveTimer  = new Stopwatch();
        private Stopwatch ConnectionTimer = new Stopwatch();
        private Stopwatch SendTimer = new Stopwatch();

        private bool StartConnection = false;
        private Timer CycleTimer;
        private RLSM SM = new RLSM("EthernetClientSM");
        protected Socket ServerSocket { get; set; }


        //public delegates
        public Action<int> ByteDataReceived { get; set; }
        public Action<State> ConnectionChanged { get; set; }
        public Action TimerTick { get; set; }
        public Action<string> ErrorOccurred { get;  set; }

        //flags and feedback
        public bool Received { get; protected set; }
        public bool SocketCreated { get; private set; }
        public bool DisconnectReceived { get; private set; } = false;
        public State ConnState {  get { return (State)SM.CurrentState; } }
        public int nrReceivedBytes { get; private set; } = 0;

        //buffers
        public byte[] rcvBuffer { get; private set; }
        
        //settings
        public int Cycletime { get; set; } = 100;

        //timeouts
        public int ConnectionTimeout { get; protected set; } = 3000;
        public int SendTimeout { get; protected set; } = 3000;
        public int ReceiveTimeout { get; protected set; } = 3000;

        //error flags
        public bool SendTimedOut { get; protected set; }
        public bool RCVTimeout { get; protected set; }
        public bool ConnectHasTimedOut { get; protected set; }

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
                SetupConn();
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
                new Transition("Disconnect", () => MonitorConnection() == false, () => { }, State.NotConnected),
            }, null, StateType.idle);

            SM.SaveGraph(@"C:\temp");
        }

        private void SetupConn()
        {
            //flags
            Received = false;
            SocketCreated = false;
            DisconnectReceived = false;
 
            //timers
            SendTimer.Reset();
            receiveTimer.Reset();
        }

        /// <summary>
        /// Method for resetting the error flags. These are not reset by the class itself
        /// </summary>
        public void ResetErrors()
        {
            SendTimedOut = false;
            RCVTimeout = false;
            ConnectHasTimedOut = false;
        }

        private bool MonitorConnection()
        {
            if (StartConnection == false || DisconnectReceived)
            {
                ErrorOccurred?.Invoke("A disconnect was reveived when waiting for data");
                Connstats.NrDisconnects++;
                return false;
            }
            if (ServerSocket.Connected == false)
            {
                ErrorOccurred?.Invoke("The socket is no longer connected");
                Connstats.NrDisconnects++;
                return false;
            }
            if (SendTimer.ElapsedMilliseconds > SendTimeout)
            {
                ErrorOccurred?.Invoke("Timeout waiting for the data to be sent");
                Connstats.NrDisconnects++;
                return false;
            }
            if (receiveTimer.ElapsedMilliseconds > ReceiveTimeout)
            {
                ErrorOccurred?.Invoke("Timeout waiting for reply");
                Connstats.NrDisconnects++;
                return false;
            }
            return true;
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
            SM.Finalize();
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
                SendTimer.Restart();
                

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
                SendTimer.Reset();
                receiveTimer.Restart();
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
            receiveTimer.Reset();
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
                //Signal the arrival of new data with the Action
                ByteDataReceived?.Invoke(nrReceivedBytes);
                receiveTimer.Restart();

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
                while (receiveTimer.ElapsedMilliseconds < ReceiveTimeout && Received == false)
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
