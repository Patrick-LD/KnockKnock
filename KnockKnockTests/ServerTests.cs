using System.Net.Sockets;
using System.Text;
using KnockKnockServer;
using Microsoft.Extensions.Configuration;

namespace KnockKnockTests
{
    // Én test pr. krav. Hver test starter sin egen server ud fra JSON og stopper den igen bagefter.
    public class ServerTests : IDisposable
    {
        private static int _nextPort = 6200;
        private int _port;
        private KnockKnockTcpServer? _server;

        [Fact]
        public void T01_En_klient_forbinder()
        {
            Start();
            var say = Connect();

            Assert.Equal("Knock! Knock!", say(null));
            Assert.Equal("Lettuce", say("Who's there?"));
        }

        [Fact]
        public void T02_Tre_klienter_samtidig()
        {
            Start();
            var a = Connect();
            var b = Connect();
            var c = Connect();

            foreach (var say in new[] { a, b, c })
            {
                Assert.Equal("Knock! Knock!", say(null));
                string? who = say("Who's there?");
                Assert.EndsWith("(y/n)", say(who + " who?"));
                Assert.Equal("Bye!", say("n"));
            }
        }

        [Fact]
        public void T03_Forkert_svar()
        {
            Start();
            var say = Connect();

            say(null);
            Assert.Equal("You are supposed to say: Who's there?", say("Hello"));
            Assert.Equal("Lettuce", say("Who's there?"));
        }

        [Fact]
        public void T04_En_klient_afbryder()
        {
            Start();
            var stays = Connect();
            stays(null);

            new TcpClient("127.0.0.1", _port).Close(); // en klient forbinder og afbryder

            Assert.Equal("Lettuce", stays("Who's there?"));
        }

        [Fact]
        public void T05_Random_strategy()
        {
            Start(strategy: "Random");
            List<string?> whos = TellJokes(20);

            Assert.True(whos.Distinct().Count() > 1);                     // ikke den samme hver gang
            Assert.NotEqual(Enumerable.Repeat(new[] { "Lettuce", "Tank" }, 10).SelectMany(x => x), whos); // ikke i rækkefølge
        }

        [Fact]
        public void T06_Sequential_strategy()
        {
            Start(strategy: "Sequential");

            Assert.Equal(new[] { "Lettuce", "Tank", "Lettuce", "Tank" }, TellJokes(4));
        }

        [Fact]
        public void T07_Ny_joke_i_json()
        {
            Start(extraJoke: """, { "Who": "Interrupting cow", "Punchline": "MOO!" }""");

            Assert.Equal(new[] { "Lettuce", "Tank", "Interrupting cow" }, TellJokes(3));
        }

        [Fact]
        public void T08_Maks_antal_klienter()
        {
            Start(maxClients: 1);
            Connect()(null);

            Assert.Equal("Server is busy. Please try again later.", Connect()(null));
        }

        [Fact]
        public void T09_Ugyldig_konfiguration()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Program.CreateServer(Config(port: 0)));

            Assert.Equal("Server:Port must be between 1 and 65535.", ex.Message);
        }

        [Fact]
        public void T10_Serveren_stoppes()
        {
            Start();
            var say = Connect();
            say(null);

            _server!.Stop();

            Assert.Null(say(null));                                              // klienten er afbrudt
            Assert.Throws<SocketException>(() => new TcpClient("127.0.0.1", _port)); // porten er lukket
        }

        // ----- Fælles kode for testene -----

        // Starter en server ud fra JSON – med præcis den samme kode som Program.cs
        private void Start(string strategy = "Sequential", int maxClients = 10, string extraJoke = "")
        {
            _port = Interlocked.Increment(ref _nextPort);
            _server = Program.CreateServer(Config(_port, strategy, maxClients, extraJoke));
            new Thread(_server.Run).Start();
            Thread.Sleep(200); // giv serveren tid til at begynde at lytte
        }

        private static IConfiguration Config(int port, string strategy = "Sequential", int maxClients = 10, string extraJoke = "")
        {
            string json = $$"""
                {
                  "Server": { "Port": {{port}}, "MaxClients": {{maxClients}} },
                  "JokeSelection": { "Strategy": "{{strategy}}" },
                  "Jokes": [
                    { "Who": "Lettuce", "Punchline": "Lettuce in!" },
                    { "Who": "Tank", "Punchline": "You're welcome!" }{{extraJoke}}
                  ]
                }
                """;
            return new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        }

        // Forbinder en klient. say("tekst") sender en linje og returnerer svaret. say(null) læser bare næste linje.
        private Func<string?, string?> Connect()
        {
            TcpClient client = new TcpClient("127.0.0.1", _port) { ReceiveTimeout = 5000 };
            StreamReader reader = new StreamReader(client.GetStream());
            StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
            return message =>
            {
                if (message != null) writer.WriteLine(message);
                return reader.ReadLine();
            };
        }

        // Fortæller et antal jokes i træk og returnerer hvem de handlede om
        private List<string?> TellJokes(int count)
        {
            var say = Connect();
            say(null);
            List<string?> whos = new List<string?>();
            for (int i = 0; i < count; i++)
            {
                string? who = say("Who's there?");
                whos.Add(who);
                say(who + " who?");
                say("y");
            }
            return whos;
        }

        // Stopper serveren efter hver test
        public void Dispose()
        {
            _server?.Stop();
        }
    }
}
