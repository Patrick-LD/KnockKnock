namespace KnockKnockServer
{
    public class RandomJokeSelector : IJokeSelector
    {
        private readonly List<KnockKnockJoke> _jokes;

        // Denne konstruktør gemmer listen med jokes
        public RandomJokeSelector(List<KnockKnockJoke> jokes)
        {
            _jokes = jokes;
        }

        // Denne metode vælger en tilfældig joke
        public KnockKnockJoke SelectJoke()
        {
            // Random.Shared er trådsikker – en delt "new Random()" er ikke
            return _jokes[Random.Shared.Next(_jokes.Count)];
        }
    }
}
