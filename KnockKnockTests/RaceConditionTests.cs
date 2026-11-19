using KnockKnockServer;

namespace KnockKnockTests
{
    // Fælles data, som flere tråde deler:
    //  - SequentialJokeSelector._nextIndex (én selector deles af alle klienter) → beskyttet af lock
    //  - KnockKnockTcpServer._clients (listen over klienter)                   → beskyttet af lock
    //  - RandomJokeSelector bruger Random.Shared                                → trådsikker i sig selv
    // Samtaletilstanden (KnockKnockProtocol) er ikke delt – hver klient har sin egen.
    public class RaceConditionTests
    {
        // Mange tråde vælger jokes samtidig. Uden lock ville nogle jokes blive valgt for ofte.
        [Fact]
        public void R01_SequentialJokeSelector_er_traadsikker()
        {
            List<KnockKnockJoke> jokes = new List<KnockKnockJoke>
            {
                new KnockKnockJoke { Who = "A" },
                new KnockKnockJoke { Who = "B" },
            };
            SequentialJokeSelector selector = new SequentialJokeSelector(jokes);

            int countA = 0;
            Parallel.For(0, 100000, i =>
            {
                if (selector.SelectJoke().Who == "A")
                {
                    Interlocked.Increment(ref countA);
                }
            });

            // Hver anden joke er A, så præcis halvdelen skal være A
            Assert.Equal(50000, countA);
        }
    }
}
