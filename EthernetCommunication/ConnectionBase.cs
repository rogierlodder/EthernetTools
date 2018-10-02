using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace EthernetCommunication
{
    public class ConnectionBase
    {
        public enum ClientState { Connected, NotConnected }

        public byte[] IncomingData;
        public byte[] OutgoingData;

        public string Name { get; private set; }
        public Socket ConnectionSocket { get; protected set; }
        public ClientState ConnState { get; protected set; } = ClientState.NotConnected;

        public IPAddress ipAddress { get; protected set; }
        public int portNr { get; protected set; }
        public bool started { get; set; } = false;
        public bool DataReceived { get; set; } = false;
        public int NrReceivedBytes { get; set; } = 0;
        public bool IsOnLoopback { get; set; } = true;
        public Action ProcessDataAction { get; set; }

        protected int bytesSent = 0;

        public ConnectionBase() { }

        /// <summary>
        /// Setup the connection with the serverIP and the locally created port number
        /// </summary>
        /// <param name="connectionSocket">Incoming socket</param>
        /// <param name="adress">Server IP address</param>
        /// <param name="port">Locally created port number</param>
        public virtual void Setup(Socket connectionSocket, IPAddress adress, int port, int bufferSize)
        {
            IncomingData = new byte[bufferSize];
            OutgoingData = new byte[bufferSize];

            ConnectionSocket = connectionSocket;
            ipAddress = adress;

            if (ipAddress.Equals(IPAddress.Loopback)) IsOnLoopback = true;
            else IsOnLoopback = false;

            portNr = port;
            Name = $"{adress.ToString()}:{port}";

            //The client is only created with an incoming connection request, so the state can be set to connected.
            ConnState = ClientState.Connected;
        }

        /// <summary>
        /// Send data asynchronously to an IP connection
        /// </summary>
        /// <param name="name">Name of the connection</param>
        /// <param name="sendData">Byte array with the data</param>
        /// <param name="nrbytes">Number of bytes to be sent</param>
        public void SendDataAsync(byte[] sendData, int nrbytes)
        {
            try
            {
                ConnectionSocket.BeginSend(sendData, 0, nrbytes, 0, new AsyncCallback(SendCallback), Name);
            }
            catch
            {
                
            }
        }

        /// <summary>
        /// Send call bak that runs when data is successfully sent
        /// </summary>
        /// <param name="ar"></param>
        void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                String S = (string)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = ConnectionSocket.EndSend(ar);
            }
            catch
            { }
        }

        /// <summary>
        /// Process the incoming data and send a reply
        /// </summary>
        public virtual void ProcessData()
        {

        }

    }
}
