namespace KnockKnockServer.States
{
    // State Pattern: hver tilstand ved selv, hvordan den reagerer på clientens besked
    public interface IConversationState
    {
        // Denne metode behandler klientens besked og returnerer serverens svar
        string Handle(KnockKnockProtocol protocol, string input);
    }
}
