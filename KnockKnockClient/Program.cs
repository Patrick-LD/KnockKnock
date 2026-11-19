using System.Net.Sockets;

namespace KnockKnockClient
{
    internal class Program
    {
        // Denne metode forbinder til serveren og sender brugerens svar frem og tilbage
        static void Main(string[] args)
        {
            using TcpClient client = new TcpClient();
            client.Connect("localhost", 6000);

            // Én UTF-8-besked pr. linje
            StreamReader reader = new StreamReader(client.GetStream());
            StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

            while (true)
            {
                string? message = reader.ReadLine();
                if (message == null)
                {
                    break; // serveren har lukket forbindelsen
                }

                Console.WriteLine("SERVER: " + message);

                if (message == "Bye!" || message == "Server is busy. Please try again later.")
                {
                    break;
                }

                string answer = "";
                while (answer.Trim() == "")
                {
                    Console.Write("Your answer: ");
                    answer = Console.ReadLine() ?? "";
                }

                writer.WriteLine(answer);
            }

            Console.WriteLine("Connection closed. Press Enter to exit.");
            Console.ReadLine();
        }
    }
}
