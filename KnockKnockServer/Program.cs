using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KnockKnockServer
{
    public class Program
    {
        // Denne metode indlæser konfigurationen, opretter serveren og starter den
        static void Main(string[] args)
        {
            // Læser automatisk appsettings.json
            IConfiguration config = Host.CreateApplicationBuilder(args).Configuration;

            KnockKnockTcpServer server;
            try
            {
                server = CreateServer(config);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("Invalid configuration in appsettings.json: " + ex.Message);
                return;
            }

            // Ctrl+C stopper serveren kontrolleret
            Console.CancelKeyPress += (sender, e) =>    
            {
                e.Cancel = true;
                server.Stop();
            };

            server.Run();
        }

        // Denne metode validerer konfigurationen og opretter serveren med den valgte strategi.
        // Den er public, så testene kan bruge præcis den samme kode.
        public static KnockKnockTcpServer CreateServer(IConfiguration config)
        {
            ServerOptions serverOptions = config.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
            JokeSelectionOptions selectionOptions = config.GetSection("JokeSelection").Get<JokeSelectionOptions>() ?? new JokeSelectionOptions();
            JokeOptions jokeOptions = config.Get<JokeOptions>() ?? new JokeOptions();

            // Stop med en forståelig besked, hvis konfigurationen er ugyldig
            if (serverOptions.Port < 1 || serverOptions.Port > 65535)
            {
                throw new InvalidOperationException("Server:Port must be between 1 and 65535.");
            }
            if (serverOptions.MaxClients < 1)
            {
                throw new InvalidOperationException("Server:MaxClients must be at least 1.");
            }
            if (selectionOptions.Strategy != "Random" && selectionOptions.Strategy != "Sequential")
            {
                throw new InvalidOperationException("JokeSelection:Strategy must be 'Random' or 'Sequential'.");
            }
            if (jokeOptions.Jokes.Count == 0)
            {
                throw new InvalidOperationException("Jokes must contain at least one joke.");
            }

            // Strategy Pattern: konfigurationen bestemmer hvilken IJokeSelector serveren får
            IJokeSelector jokeSelector;
            if (selectionOptions.Strategy == "Sequential")
            {
                jokeSelector = new SequentialJokeSelector(jokeOptions.Jokes);
            }
            else
            {
                jokeSelector = new RandomJokeSelector(jokeOptions.Jokes);
            }

            return new KnockKnockTcpServer(serverOptions, jokeSelector);
        }
    }
}
