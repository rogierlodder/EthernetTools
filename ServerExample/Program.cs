using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EthernetCommunication;

namespace ServerExample
{
    class Program
    {
        static void Main(string[] args)
        {
            Encoding Enc = Encoding.ASCII;
            string reply = "Great, thanks!";
            var Server = new CEthernetServer<ConnectionBase>("MyServ", "127.0.0.1", 16669, "TCP", 512);
            var connection = new ConnectionBase();

            Server.NewConnection = p =>
            {
                Console.WriteLine($"A new client connected: {p}");
                connection = Server.AllConnectionsList.Where(q => q.Name == p).First();

                connection.ProcessDataAction = () =>
                {
                    var text = Enc.GetString(connection.IncomingData);
                    Console.WriteLine($"Incoming data: {text.Substring(0, connection.NrReceivedBytes)}");
                    connection.SendDataAsync(Enc.GetBytes(reply), reply.Length);
                };
            };

            Console.ReadLine();
        }
    }
}
