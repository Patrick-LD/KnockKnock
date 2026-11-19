using KnockKnockServer.States;

namespace KnockKnockServer
{
    // Ansvarlig for selve Knock Knock-protokollen. Ved intet om sockets.
    // Hver klient får sin egen instans, så samtaletilstanden ikke deles mellem tråde.
    public class KnockKnockProtocol
    {
        private readonly IJokeSelector _jokeSelector;

        // Denne konstruktør gemmer den strategi, der bruges til at vælge jokes
        public KnockKnockProtocol(IJokeSelector jokeSelector)
        {
            _jokeSelector = jokeSelector;
        }

        public IConversationState State { get; set; } = new WaitingForWhosThereState();
        public KnockKnockJoke CurrentJoke { get; private set; } = new KnockKnockJoke();
        public bool IsDone { get; set; }

        // Denne metode henter en ny joke og starter samtalen forfra
        public string Start()
        {
            CurrentJoke = _jokeSelector.SelectJoke();
            State = new WaitingForWhosThereState();
            return "Knock! Knock!";
        }

        // Denne metode sender klientens besked videre til den aktuelle tilstand
        public string ProcessInput(string input)
        {
            return State.Handle(this, input);
        }

        // Denne metode sammenligner svaret 
        public static bool Matches(string input, string expected)
        {
            return string.Equals(input.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
