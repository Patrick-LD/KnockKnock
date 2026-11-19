using System.Net.Sockets;

namespace KnockKnockServer
{
    // Snakker med én klient. Ved intet om Knock Knock-reglerne – det klarer protokollen.
    public class ClientHandler
    {
        private readonly int _id;
        private readonly TcpClient _client;
        private readonly KnockKnockProtocol _protocol;

        // Denne konstruktør gemmer klientens nummer, forbindelse og protokol
        public ClientHandler(int id, TcpClient client, KnockKnockProtocol protocol)
        {
            _id = id;
            _client = client;
            _protocol = protocol;
        }

        // Denne metode kører hele samtalen med klienten på klientens egen tråd
        public void Run()
        {
            Console.WriteLine($"[Client {_id}] Connected");
            try
            {
                // Én UTF-8-besked pr. linje
                StreamReader reader = new StreamReader(_client.GetStream());
                StreamWriter writer = new StreamWriter(_client.GetStream()) { AutoFlush = true };

                Send(writer, _protocol.Start());

                while (!_protocol.IsDone)
                {
                    string? input = reader.ReadLine();
                    if (input == null)
                    {
                        break; // klienten lukkede forbindelsen
                    }

                    Console.WriteLine($"[Client {_id}] CLIENT: {input}");
                    Send(writer, _protocol.ProcessInput(input));
                }
            }
            catch (Exception)
            {
                // Fejl må kun ramme denne klient – ikke hele serveren
                Console.WriteLine($"[Client {_id}] Connection lost");
            }
            finally
            {
                _client.Close();
                Console.WriteLine($"[Client {_id}] Disconnected");
            }
        }

        // Denne metode sender én besked til klienten og skriver den i konsollen
        private void Send(StreamWriter writer, string message)
        {
            Console.WriteLine($"[Client {_id}] SERVER: {message}");
            writer.WriteLine(message);
        }
    }
}
