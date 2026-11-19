namespace KnockKnockServer
{
    // Strategy Pattern: fælles interface for alle måder at vælge en joke på.
    // Én instans deles af alle klienttråde, så implementeringerne skal være trådsikre.
    public interface IJokeSelector
    {
        // Denne metode returnerer den næste joke
        KnockKnockJoke SelectJoke();
    }
}
