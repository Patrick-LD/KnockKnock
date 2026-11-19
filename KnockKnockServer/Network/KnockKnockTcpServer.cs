using System.Net;
using System.Net.Sockets;

namespace KnockKnockServer
{
    // Accepterer forbindelser og starter en tråd pr. klient. Ved intet om jokes eller samtalens regler.
    public class KnockKnockTcpServer
    {
        private readonly ServerOptions _options;
        private readonly IJokeSelector _jokeSelector;

        // Delt mellem hovedtråden og klienttrådene, så den beskyttes af _lock
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private readonly object _lock = new object();

        private TcpListener? _listener;

        // Denne konstruktør får konfigurationen og joke-strategien udefra
        public KnockKnockTcpServer(ServerOptions options, IJokeSelector jokeSelector)
        {
            _options = options;
            _jokeSelector = jokeSelector;
        }

        // Denne metode lytter efter klienter og starter en ny tråd til hver, indtil Stop() kaldes
        public void Run()
        {
            _listener = new TcpListener(IPAddress.Loopback, _options.Port);
            _listener.Start();
            Console.WriteLine($"Knock Knock Server started on port {_options.Port} (max {_options.MaxClients} clients). Press Ctrl+C to stop.");

            int clientId = 0;
            while (true)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    break; // Stop() lukkede listeneren, mens den ventede på en klient
                }
                catch (InvalidOperationException)
                {
                    break; // Stop() lukkede listeneren, mens den var i gang med en anden klient
                }

                clientId++;
                lock (_lock)
                {
                    if (_clients.Count >= _options.MaxClients)
                    {
                        Console.WriteLine($"[Client {clientId}] Rejected: server is full");
                        Reject(client);
                        continue;
                    }
                    _clients.Add(client);
                }

                // Hver klient får sin egen protokol (egen samtaletilstand), men deler joke-strategien
                ClientHandler handler = new ClientHandler(clientId, client, new KnockKnockProtocol(_jokeSelector));
                new Thread(() =>
                {
                    handler.Run();
                    lock (_lock)
                    {
                        _clients.Remove(client);
                    }
                }).Start();
            }

            Console.WriteLine("Server stopped");
        }

        // Denne metode stopper serveren og lukker alle åbne forbindelser
        public void Stop()
        {
            Console.WriteLine("Stopping server...");
            _listener?.Stop();

            // Lukker alle åbne forbindelser, så klienttrådene slutter
            lock (_lock)
            {
                foreach (TcpClient client in _clients)
                {
                    client.Close();
                }
            }
        }

        // Denne metode sender "server is busy" til klienten og lukker forbindelsen
        private static void Reject(TcpClient client)
        {
            try
            {
                StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                writer.WriteLine("Server is busy. Please try again later.");
            }
            catch (IOException)
            {
                // Klienten er allerede væk
            }
            client.Close();
        }
    }
}
