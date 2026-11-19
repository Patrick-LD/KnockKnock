namespace KnockKnockServer.States
{
    public class WaitingForWhoState : IConversationState
    {
        // Denne metode tjekker om klienten svarer "<Who> who?" og sender punchline
        public string Handle(KnockKnockProtocol protocol, string input)
        {
            string expected = protocol.CurrentJoke.Who + " who?";

            if (KnockKnockProtocol.Matches(input, expected))
            {
                protocol.State = new FinishedState();
                // Én besked pr. linje, så punchline og spørgsmål sendes på samme linje
                return protocol.CurrentJoke.Punchline + " Want another joke? (y/n)";
            }

            return "You are supposed to say: " + expected;
        }
    }
}
