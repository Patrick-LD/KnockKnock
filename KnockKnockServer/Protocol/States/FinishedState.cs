namespace KnockKnockServer.States
{
    public class FinishedState : IConversationState
    {
        // Denne metode starter en ny joke ved "y" eller afslutter samtalen ved "n"
        public string Handle(KnockKnockProtocol protocol, string input)
        {
            string answer = input.Trim().ToLower();

            if (answer == "y" || answer == "yes")
            {
                // Start en ny joke
                return protocol.Start();
            }

            if (answer == "n" || answer == "no")
            {
                protocol.IsDone = true;
                return "Bye!";
            }

            return "Please answer y or n. Want another joke?";
        }
    }
}
