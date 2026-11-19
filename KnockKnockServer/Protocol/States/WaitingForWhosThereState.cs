namespace KnockKnockServer.States
{
    public class WaitingForWhosThereState : IConversationState
    {
        // Denne metode tjekker om klienten svarer "Who's there?" og sender jokens Who
        public string Handle(KnockKnockProtocol protocol, string input)
        {
            if (KnockKnockProtocol.Matches(input, "Who's there?"))
            {
                protocol.State = new WaitingForWhoState();
                return protocol.CurrentJoke.Who;
            }

            // Forkert svar: bliv i samme tilstand
            return "You are supposed to say: Who's there?";
        }
    }
}
