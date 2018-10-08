using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EthernetCommunication
{
    public class CEthernetServer<TConnection> : CEthernetDevice where TConnection : ConnectionBase, new()
    {
        private Socket Listener;
        private int ConnectionBufSize = 65536;
        private Timer CleanupTimer;

        //actions
        public Action<ConnectionBase> NewConnection { get; set; }
        public Action<string, byte[], int> NewDataReceived { get; set; }
        public Action<string, string> ReportError { get; set; }

        //connections
        public Dictionary<string, TConnection> AllConnections { get; protected set; } = new Dictionary<string, TConnection>();
        public List<TConnection> AllConnectionsList { get; protected set; } = new List<TConnection>();
        public int NrConnections { get { return AllConnections.Count; } }

        public CEthernetServer(string name, string ip, int port, string socktype, int bufsize, long cleanupinterval)
        {
            IPAddress IP;
            if (IPAddress.TryParse(ip, out IP)) IPaddress = IP; else return;

            Port = port;
            ConnectionName = name;
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

            IPEndPoint localEndPoint = new IPEndPoint(IPaddress, Port);

            // Create a TCP/IP socket.
            Listener = new Socket(AddressFamily.InterNetwork, SockType, ProtType);
            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                Listener.Bind(localEndPoint);
                Listener.Listen(100);
                // Start an asynchronous socket to listen for connections.
                Listener.BeginAccept(new AsyncCallback(AcceptConnectCallback), Listener);
            }
            catch
            { }
            ConnectionBufSize = bufsize;

            CleanupTimer = new Timer(CleanupConnections);
            CleanupTimer.Change(0, cleanupinterval*1000);
        }

        public void SendDataAsync(string name, byte[] sendData, int nrbytes)
        {
            TConnection C = AllConnections[name];
            try
            {
                C.ConnectionSocket.BeginSend(C.IncomingData, 0, C.IncomingData.Length, 0, new AsyncCallback(SendCallback), C.Name);
            }
            catch { }
        }

        public void RemoveConnection(string name)
        {
            if (!AllConnections.ContainsKey(name)) return;

            TConnection Conn = AllConnections[name];
            AllConnectionsList.Remove(Conn);

            Conn.IncomingData = null;
            Conn.OutgoingData = null;

            AllConnections.Remove(name);
        }

        public void DisconnectAll()
        {
            foreach (var C in AllConnections.Values)
            {
                try
                {
                    C.ConnectionSocket.Disconnect(false);
                }
                catch { }

            }
            ClearAllConnections();
        }

        private void AcceptConnectCallback(IAsyncResult ar)
        {
            Listener = (Socket)ar.AsyncState;
            
            //acknowledge the connection
            Socket incomingSocket = null;
            try
            {
                incomingSocket = Listener.EndAccept(ar);
            }
            catch
            {
                ReportError?.Invoke("EndAccept failed on incoming connection","");
            }

            //put the listener back to listening
            Listener.BeginAccept(new AsyncCallback(AcceptConnectCallback), Listener);

            if (incomingSocket == null) return;
            IPEndPoint ep = (IPEndPoint)incomingSocket.RemoteEndPoint;

            TConnection C = new TConnection();
            C.Setup(incomingSocket, ep.Address, ep.Port, ConnectionBufSize);

            if (AllConnections.ContainsKey(C.Name) == false) AddConnection(C);

            //Signal that a new connection has been created
            NewConnection?.Invoke(C);

            //configure the socket to receive incoming data and arm the data reception event
            try
            {
                C.ConnectionSocket.BeginReceive(C.IncomingData, 0, C.IncomingData.Length, 0, new AsyncCallback(ReadCallback), C.Name);
            }
            catch
            {
                ReportError?.Invoke("BeginReceive failed on new connection", C.Name);
            }
        }

        private void AddConnection(TConnection C)
        {
            AllConnections.Add(C.Name, C);
            AllConnectionsList.Add(C);
        }
        private void ClearAllConnections()
        {
            AllConnections.Clear();
            AllConnectionsList.Clear();
        }

        private void ReadCallback(IAsyncResult ar)
        {
            //get the client from the asynchronous state object
            TConnection C = null;
            try
            {
                C = AllConnections[(string)ar.AsyncState];
            }
            catch
            {
                ReportError("ReadCallback received from a client that is no longer in the database", "");

                return;
            }

            int bytesread = 0;
            try
            {
                bytesread = C.ConnectionSocket.EndReceive(ar); //acknowledge the data receipt
            }
            catch
            {
                ReportError?.Invoke("EndReceive failed during ReadCallback", C.Name);
            }
            if (bytesread > 0)
            {
                C.ConnStats.LastComm.Restart();
                C.ConnStats.Receivedpackets++;
                //call external function to process the data
                NewDataReceived?.Invoke(C.Name, C.IncomingData, bytesread);
                C.NrReceivedBytes = bytesread;

                //call the data processing method of the connection itself
                C.ProcessData();
                C.ProcessDataAction?.Invoke();

                //signal the arrival of data
                C.DataReceived = true;

                //set the socket back to listening mode
                try
                {
                    C.ConnectionSocket.BeginReceive(C.IncomingData, 0, C.IncomingData.Length, 0, new AsyncCallback(ReadCallback), C.Name);
                }
                catch
                {
                    ReportError?.Invoke("BeginReceive failed during ReadCallback", C.Name);
                }
            }
            else //if a received data event arrives with no data, a disconnect notification was received
            {
                //Handle disconnect
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            var name = "";
            try
            {
                // Retrieve the socket from the state object.
                TConnection C = AllConnections[(string)ar.AsyncState];
                name = C.Name;

                // Complete sending the data to the remote device.
                int bytesSent = C.ConnectionSocket.EndSend(ar);
            }
            catch
            {
                ReportError?.Invoke("EndSend failed during SendCallback", name);
            }
        }

        private void CleanupConnections(object sender)
        {
            for (int i=0; i< AllConnectionsList.Count; i++)
            {
                var C = AllConnectionsList[i];
                if (C.ConnStats.HasTimedOut)
                {
                    AllConnectionsList.Remove(C);
                    AllConnections.Remove(C.Name);
                }
            }
        }

        #region Tools
        /// <summary>
        /// A simple test for checking if a combination of IP address and Netmask can connect to this host.
        /// </summary>
        /// <param name="ipaddress"></param>
        /// <param name="netmask"></param>
        /// <returns></returns>
        public static bool CanConnectIPv4(string ip)
        {
            UInt32 clientIP;
            try
            {
                clientIP = GetUint(IPAddress.Parse(ip));
            }
            catch { return false; }

            var hosts = GetHostAddresses(true);
            bool matchFound = false;
            foreach (var K in hosts)
            {
                UInt32 hostIP = GetUint(K.Key);
                UInt32 hostNM = GetUint(K.Value);

                if ((hostIP & hostNM) == (clientIP & hostNM)) matchFound = true;
            }
            return matchFound;
        }

        /// <summary>
        /// Get the unsigned int value of the address
        /// </summary>
        /// <param name="ip">IP address input</param>
        /// <returns></returns>
        private static UInt32 GetUint(IPAddress ip)
        {
            byte[] buf = ip.GetAddressBytes();
            //reverse to little endian first
            buf = buf.Reverse().ToArray();
            return BitConverter.ToUInt32(buf, 0);
        }

        /// <summary>
        /// returns a string dictionary with the hosts' ipaddresses and netmasks
        /// </summary>
        /// <param name="IPv4Only">If IPv4Only is true, only IPv4 addresses are returned</param>
        /// <returns></returns>
        public static Dictionary<IPAddress, IPAddress> GetHostAddresses(bool IPv4Only)
        {
            var D = new Dictionary<IPAddress, IPAddress>();

            IPHostEntry ThisHost = Dns.GetHostEntry(Dns.GetHostName()); //get local IP address
            foreach (var S in ThisHost.AddressList)
            {
                //only add IPv4 addresses
                if (IPv4Only)
                {
                    if (S.AddressFamily == AddressFamily.InterNetwork) D.Add(S, GetSubnetMask(S));
                }
                else D.Add(S, GetSubnetMask(S));
            }
            return D;
        }

        /// <summary>
        /// Routine for getting the net mask of the network adapter
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public static IPAddress GetSubnetMask(IPAddress address)
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (UnicastIPAddressInformation unicastIPAddressInformation in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicastIPAddressInformation.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (address.Equals(unicastIPAddressInformation.Address))
                        {
                            return unicastIPAddressInformation.IPv4Mask;
                        }
                    }
                }
            }
            return IPAddress.Parse("0.0.0.0"); //netmask not found
        }
        #endregion
    }
}