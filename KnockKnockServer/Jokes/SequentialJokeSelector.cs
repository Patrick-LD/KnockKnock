namespace KnockKnockServer
{
    public class SequentialJokeSelector : IJokeSelector
    {
        private readonly List<KnockKnockJoke> _jokes;
        private readonly object _lock = new object();
        private int _nextIndex;

        // Denne konstruktør gemmer listen med jokes
        public SequentialJokeSelector(List<KnockKnockJoke> jokes)
        {
            _jokes = jokes;
        }

        // Denne metode vælger den næste joke i rækkefølge og starter forfra efter den sidste
        public KnockKnockJoke SelectJoke()
        {
            // _nextIndex deles af alle klienttråde. Uden lock kan to klienter
            // læse samme index og få samme joke (race condition).
            lock (_lock)
            {
                KnockKnockJoke joke = _jokes[_nextIndex];
                _nextIndex = (_nextIndex + 1) % _jokes.Count;
                return joke;
            }
        }
    }
}
