using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EthernetCommunication;

namespace ClientExample
{
    class Program
    {
        static Encoding Enc = Encoding.ASCII;
        static byte[] sendBuf = new byte[256];
        static byte[] receiveBuf = new byte[256];
        static string request = "Hey, how is it going?";
        static CEthernetClient EC = new CEthernetClient("TestClient");

        static async Task Run()
        {
            while (true)
            {
                await EC.ConnectedTask();

                while (EC.ConnState == CEthernetDevice.State.Connected)
                {
                    EC.SendData(sendBuf, sendBuf.Length, receiveBuf);
                    Console.WriteLine($"Sending data: {request}");

                    await EC.ReplyTask();

                    await Task.Delay(100);
                }
            }
        }

        static void Main(string[] args)
        {
            sendBuf = Enc.GetBytes(request);

            EC.SetConnection("127.0.01", 16669, "TCP");
            EC.ConnectAndStart();

            EC.ConnectionChanged = p => Console.WriteLine($"New State: {p.ToString()}");

            EC.ByteDataReceived = p => Console.WriteLine(Enc.GetString(receiveBuf).Substring(0, p));

            Run();

            Console.ReadLine();
        }
    }
}
