namespace KnockKnockServer
{
    // Klasserne svarer til sektionerne i appsettings.json (Options Pattern)

    public class ServerOptions
    {
        public int Port { get; set; }
        public int MaxClients { get; set; }
    }

    public class JokeSelectionOptions
    {
        public string Strategy { get; set; } = "";
    }

    public class JokeOptions
    {
        public List<KnockKnockJoke> Jokes { get; set; } = new List<KnockKnockJoke>();
    }
}
