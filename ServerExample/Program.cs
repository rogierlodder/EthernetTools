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
            string reply = "This is the reply form the server";
            var Server = new CEthernetServer<ConnectionBase>("MyServ", "127.0.0.1", 16669, "TCP", 512, 10);

            Server.ReportError = (p,n) => Console.WriteLine($"Error: {p}, on: {n}");

            Server.NewConnection = connection =>
            {
                Console.WriteLine($"A new client connected: {connection}");
                connection.ConnStats.ConnectionTimeout = 5;

                connection.ProcessDataAction = () =>
                {
                    var text = Enc.GetString(connection.IncomingData);
                    Console.WriteLine($"Incoming data({Server.NrConnections}): {text.Substring(0, connection.NrReceivedBytes)}");
                    connection.SendDataAsync(Enc.GetBytes(reply), reply.Length);
                };
            };

            Console.ReadLine();
        }
    }
}
