# Forklaring af min Knock Knock Server

Her gennemgår jeg alle filerne i løsningen, viser de vigtigste stykker kode og forklarer hvad de gør og hvorfor. Jeg starter med det overordnede billede, så hver fil for sig og til sidst trådene, race conditions og SOLID.

## Indhold

1. [Det overordnede billede](#det-overordnede-billede)
2. [appsettings.json](#appsettingsjson)
3. [KnockKnockServer.csproj](#knockknockservercsproj)
4. [Options.cs](#optionscs)
5. [KnockKnockJoke.cs](#knockknockjokecs)
6. [Program.cs](#programcs)
7. [KnockKnockTcpServer.cs](#knockknocktcpservercs)
8. [ClientHandler.cs](#clienthandlercs)
9. [KnockKnockProtocol.cs](#knockknockprotocolcs)
10. [Protocol/States/ (State Pattern)](#protocolstates-state-pattern)
11. [IJokeSelector, RandomJokeSelector, SequentialJokeSelector (Strategy Pattern)](#ijokeselector-randomjokeselector-sequentialjokeselector-strategy-pattern)
12. [Klienten](#klienten-knockknockclientprogramcs)
13. [Diagrammerne](#diagrammerne-docs)
14. [Tråde og race conditions](#tråde-og-race-conditions)
15. [SOLID](#solid)
16. [Mine valg, og hvad jeg kunne have gjort i stedet](#mine-valg-og-hvad-jeg-kunne-have-gjort-i-stedet)
17. [Sådan demonstrerer jeg det](#sådan-demonstrerer-jeg-det)

---

## Det overordnede billede

Når serveren starter, læser `Program.cs` konfigurationen fra `appsettings.json` tjekker, at den er gyldig, og opretter alle objekterne med `new`. Derefter står serveren på hovedtråden og lytter efter nye forbindelser. Hver gang en klient forbinder, starter serveren en ny tråd, og den tråd snakker med netop den klient gennem en `ClientHandler`. Hver klient får sit eget `KnockKnockProtocol`-objekt, som holder styr på hvor langt samtalen er nået. Når protokollen skal bruge en joke, spørger den en `IJokeSelector`. Hvilken selector det er, Random eller Sequential, står i konfigurationen.

```
Klient 1 ──TCP──┐
Klient 2 ──TCP──┼──> KnockKnockTcpServer (hovedtråd, lytter)
Klient 3 ──TCP──┘          │
                           ├── Thread ──> ClientHandler ──> KnockKnockProtocol ──> IJokeSelector (delt)
                           ├── Thread ──> ClientHandler ──> KnockKnockProtocol ──┘
                           └── Thread ──> ClientHandler ──> KnockKnockProtocol ──┘
```

Jeg har delt ansvaret op i tre lag:

- **Netværk:** `KnockKnockTcpServer` og `ClientHandler` håndterer TCP og tråde, men ved intet om jokes.
- **Samtalelogik:** `KnockKnockProtocol` og tilstandene i `Protocol/States/` kender reglerne for en Knock Knock-joke, men ved intet om sockets.
- **Valg af joke:** `IJokeSelector` og dens to implementeringer bestemmer hvilken joke der bruges.

---

## `appsettings.json`

```json
{
  "Server": {
    "Port": 6000,
    "MaxClients": 10
  },
  "JokeSelection": {
    "Strategy": "Random"
  },
  "Jokes": [
    { "Who": "Lettuce", "Punchline": "Lettuce in, it's cold out here!" },
    { "Who": "Tank",    "Punchline": "You're welcome!" },
    ...
  ]
}
```

Her ligger al konfigurationen: hvilken port serveren lytter på, hvor mange klienter der må være forbundet på én gang, hvilken strategi der bruges til at vælge jokes, og selve jokene. Der er seks jokes, så kravet om mindst fem er opfyldt.

Der er ingen jokes i C#-koden længere. Vil man tilføje en ny joke, skriver man den ind i arrayet og genstarter serveren. Man behøver ikke ændre eller genkompilere koden. Det samme gælder, hvis man vil skifte strategi: man ændrer `"Random"` til `"Sequential"` og genstarter.

---

## `KnockKnockServer.csproj`

```xml
<PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.10" />

<None Update="appsettings.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

Projektfilen har to vigtige ting:

- **NuGet-pakken `Microsoft.Extensions.Hosting`** giver mig Microsofts Configuration API, som læser `appsettings.json` og fylder værdierne ind i mine klasser.
- **`CopyToOutputDirectory`** kopierer `appsettings.json` med ud i `bin`-mappen, når projektet bygges, så programmet kan finde filen, når det kører.

---

## `Options.cs`

```csharp
public class ServerOptions
{
    public int Port { get; set; }
    public int MaxClients { get; set; }
}

public class JokeSelectionOptions
{
    public string Strategy { get; set; } = "";
}

public class JokeOptions
{
    public List<KnockKnockJoke> Jokes { get; set; } = new List<KnockKnockJoke>();
}
```

Tre små klasser, der hver svarer til en del af `appsettings.json`. Property-navnene skal hedde det samme som nøglerne i JSON-filen, fordi Microsofts binder matcher dem på navn. Fx bliver `"Port": 6000` automatisk lagt ind i `ServerOptions.Port` som en `int`.

Det kaldes stærkt typede options. Pointen er, at resten af programmet aldrig selv læser eller fortolker JSON. De får bare et færdigt objekt med de rigtige typer. Skriver jeg `options.Port` forkert, får jeg en compile-fejl, hvor jeg med en tekststreng som `config["Server:Port"]` først ville opdage fejlen, når programmet kører.

---

## `KnockKnockJoke.cs`

```csharp
public class KnockKnockJoke
{
    public string Who { get; set; } = "";
    public string Punchline { get; set; } = "";
}
```

En simpel dataklasse med to properties: `Who`, som er det svar serveren giver på "Who's there?", og `Punchline`. Hvert objekt i `"Jokes"`-arrayet i JSON bliver automatisk til én `KnockKnockJoke`.

---

## `Program.cs`

Det er her alt bliver sat sammen. Jeg gennemgår den i fem trin.

### 1. Læs konfigurationen

```csharp
IConfiguration config = Host.CreateApplicationBuilder(args).Configuration;
```

`Host.CreateApplicationBuilder` læser automatisk `appsettings.json` og gør det også muligt at overskrive værdier fra kommandolinjen, fx `--JokeSelection:Strategy=Sequential`. Jeg bruger kun dens `Configuration`, altså den samlede konfiguration. Jeg bruger ikke dens dependency injection, så alle objekter bliver oprettet med `new` længere nede.

### 2. Bind sektionerne til klasser

```csharp
ServerOptions serverOptions = config.GetSection("Server").Get<ServerOptions>() ?? new ServerOptions();
JokeSelectionOptions selectionOptions = config.GetSection("JokeSelection").Get<JokeSelectionOptions>() ?? new JokeSelectionOptions();
JokeOptions jokeOptions = config.Get<JokeOptions>() ?? new JokeOptions();
```

- **`GetSection("Server")`** tager kun `"Server"`-blokken fra JSON-filen.
- **`.Get<T>()`** opretter et objekt af klassen og fylder værdierne ind, hvor property-navnene matcher nøglerne i JSON.
- **`JokeOptions`** binder jeg til hele konfigurationen, fordi `"Jokes"` ligger i roden af filen, og så matcher property-navnet `Jokes` arrayet direkte.
- **`?? new ...()`**: `.Get<T>()` returnerer `null`, hvis sektionen slet ikke findes. Så bruger jeg et tomt objekt i stedet, og valideringen herunder fanger, at værdierne mangler.

### 3. Validering

```csharp
string? error = null;
if (serverOptions.Port < 1 || serverOptions.Port > 65535)
{
    error = "Server:Port must be between 1 and 65535.";
}
else if (serverOptions.MaxClients < 1)
{
    error = "Server:MaxClients must be at least 1.";
}
else if (selectionOptions.Strategy != "Random" && selectionOptions.Strategy != "Sequential")
{
    error = "JokeSelection:Strategy must be 'Random' or 'Sequential'.";
}
else if (jokeOptions.Jokes.Count == 0)
{
    error = "Jokes must contain at least one joke.";
}

if (error != null)
{
    Console.WriteLine("Invalid configuration in appsettings.json: " + error);
    return;
}
```

Almindelige `if`-tjek, som stopper ved den første fejl. Er konfigurationen ugyldig, skriver serveren en forståelig besked og stopper, i stedet for at crashe senere med en stack trace. Fx:

```
Invalid configuration in appsettings.json: JokeSelection:Strategy must be 'Random' or 'Sequential'.
```

Porten skal ligge mellem 1 og 65535, fordi et portnummer fylder 16 bit, og port 0 betyder "vælg en tilfældig ledig port".

### 4. Strategy Pattern: vælg strategi og opret serveren

```csharp
IJokeSelector jokeSelector;
if (selectionOptions.Strategy == "Sequential")
{
    jokeSelector = new SequentialJokeSelector(jokeOptions.Jokes);
}
else
{
    jokeSelector = new RandomJokeSelector(jokeOptions.Jokes);
}

KnockKnockTcpServer server = new KnockKnockTcpServer(serverOptions, jokeSelector);
```

Konfigurationen bestemmer, hvilken `IJokeSelector` der bliver oprettet. Variablen har typen `IJokeSelector`, så resten af programmet ved ikke, hvilken af de to klasser det er.

Der bliver kun oprettet én selector, som serveren giver videre til alle klienter. Det er nødvendigt for `SequentialJokeSelector`, for ellers ville hver klient have sin egen tæller og altid starte med den første joke. Men det betyder også, at selectoren bliver brugt af flere tråde på én gang og derfor skal være trådsikker.

Det er det eneste sted i hele programmet, hvor man skal ændre noget, hvis man tilføjer en ny strategi.

### 5. Ctrl+C og start

```csharp
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    server.Stop();
};

server.Run();
```

Normalt dræber Ctrl+C processen med det samme. Ved at sætte `e.Cancel = true` forhindrer jeg det og kalder i stedet `server.Stop()`, så serveren selv lukker forbindelserne pænt. Event-handleren kører på en anden tråd end hovedtråden. Til sidst kalder jeg `server.Run()`, som blokerer hovedtråden, indtil serveren er stoppet. Så slutter `Main`, og programmet afslutter.

---

## `KnockKnockTcpServer.cs`

Den klasse, der står for netværket på serversiden. Den ved intet om jokes eller samtalens regler.

### Felter og konstruktør

```csharp
private readonly ServerOptions _options;
private readonly IJokeSelector _jokeSelector;

private readonly List<TcpClient> _clients = new List<TcpClient>();
private readonly object _lock = new object();

private TcpListener? _listener;

public KnockKnockTcpServer(ServerOptions options, IJokeSelector jokeSelector)
{
    _options = options;
    _jokeSelector = jokeSelector;
}
```

Konstruktøren får sine afhængigheder ind udefra. Det er `Program.cs`, der opretter dem og giver dem videre. Den afhænger af interfacet `IJokeSelector` og ikke af en bestemt strategi. `_clients` er listen over forbundne klienter, og `_lock` er et helt almindeligt objekt, som kun bruges som "nøgle" til `lock`.

### Run: hovedtrådens løkke

```csharp
public void Run()
{
    _listener = new TcpListener(IPAddress.Loopback, _options.Port);
    _listener.Start();

    int clientId = 0;
    while (true)
    {
        TcpClient client;
        try
        {
            client = _listener.AcceptTcpClient();
        }
        catch (SocketException)
        {
            break; // Stop() har lukket listeneren
        }
        ...
```

`TcpListener` lytter på den port, der står i konfigurationen. `IPAddress.Loopback` betyder, at den kun accepterer forbindelser fra den samme computer (localhost). `AcceptTcpClient()` blokerer, altså venter, indtil en klient forbinder, og returnerer så en `TcpClient` for den forbindelse.

Når `Stop()` lukker listeneren, kaster den ventende `AcceptTcpClient()` en `SocketException`. Det bruger jeg som signal til at bryde ud af løkken.

### Maksimalt antal klienter

```csharp
        clientId++;
        lock (_lock)
        {
            if (_clients.Count >= _options.MaxClients)
            {
                Console.WriteLine($"[Client {clientId}] Rejected: server is full");
                Reject(client);
                continue;
            }
            _clients.Add(client);
        }
```

Før en ny klient får en tråd, tjekker jeg, om der allerede er `MaxClients` forbundne. Tjekket og `Add` står inde i samme `lock`, så de sker som én samlet handling. Ellers kunne en klienttråd fjerne sig selv fra listen lige midt imellem, og så ville tallet, jeg tjekkede på, ikke længere passe.

### Start en tråd pr. klient

```csharp
        ClientHandler handler = new ClientHandler(clientId, client, new KnockKnockProtocol(_jokeSelector));
        new Thread(() =>
        {
            handler.Run();
            lock (_lock)
            {
                _clients.Remove(client);
            }
        }).Start();
    }
}
```

Her er selve multithreadingen. For hver klient opretter jeg:

- en `ClientHandler`, som står for kommunikationen,
- et **nyt** `KnockKnockProtocol`, så klienten får sin egen samtaletilstand,
- en ny `Thread`, som kører `handler.Run()`.

Det jeg giver `Thread` er en lambda, altså en lille anonym metode. Den kører samtalen og fjerner bagefter klienten fra listen igen, så der bliver plads til en ny. `.Start()` starter tråden, og hovedtråden fortsætter med det samme tilbage til toppen af løkken og venter på den næste klient. Derfor kan serveren acceptere nye forbindelser, mens andre klienter er midt i deres samtale.

Læg mærke til, at alle klienter får den samme `_jokeSelector`. Protokollen er pr. klient, men strategien er delt.

### Stop: kontrolleret nedlukning

```csharp
public void Stop()
{
    Console.WriteLine("Stopping server...");
    _listener?.Stop();

    lock (_lock)
    {
        foreach (TcpClient client in _clients)
        {
            client.Close();
        }
    }
}
```

`Stop()` bliver kaldt fra Ctrl+C-tråden. Først stopper den listeneren, så `Run()`-løkken slutter. Derefter lukker den alle åbne klientforbindelser. Når en forbindelse lukkes, kaster den ventende `ReadLine()` i `ClientHandler` en exception, klientens tråd rydder op og slutter. `?.` betyder "kun hvis `_listener` ikke er null".

### Reject: afvis kontrolleret

```csharp
private static void Reject(TcpClient client)
{
    try
    {
        StreamWriter writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
        writer.WriteLine("Server is busy. Please try again later.");
    }
    catch (IOException)
    {
        // Klienten er allerede væk
    }
    client.Close();
}
```

En afvist klient bliver ikke bare smidt af. Den får en besked om, at serveren er fuld, og så lukkes forbindelsen. Hvis klienten allerede er væk, fanger jeg fejlen, så den ikke stopper hovedtråden.

---

## `ClientHandler.cs`

Står for kommunikationen med én enkelt klient. `Run()` kører på klientens egen tråd. Den ved intet om Knock Knock-reglerne. Den sender bare beskeder frem og tilbage mellem netværket og protokollen.

```csharp
public void Run()
{
    Console.WriteLine($"[Client {_id}] Connected");
    try
    {
        StreamReader reader = new StreamReader(_client.GetStream());
        StreamWriter writer = new StreamWriter(_client.GetStream()) { AutoFlush = true };

        Send(writer, _protocol.Start());

        while (!_protocol.IsDone)
        {
            string? input = reader.ReadLine();
            if (input == null)
            {
                break; // klienten lukkede forbindelsen
            }

            Console.WriteLine($"[Client {_id}] CLIENT: {input}");
            Send(writer, _protocol.ProcessInput(input));
        }
    }
    catch (Exception)
    {
        Console.WriteLine($"[Client {_id}] Connection lost");
    }
    finally
    {
        _client.Close();
        Console.WriteLine($"[Client {_id}] Disconnected");
    }
}
```

**Én besked pr. linje.** `StreamReader` og `StreamWriter` lægges oven på netværksstrømmen og bruger UTF-8 som standard. `WriteLine` sender en besked efterfulgt af et linjeskift, og `ReadLine` læser præcis én linje. På den måde er det tydeligt, hvor én besked slutter og den næste begynder. Med rå `stream.Read` kunne to beskeder klistre sammen, eller én besked blive delt i to. `AutoFlush = true` sørger for, at beskeden sendes med det samme i stedet for at ligge i en buffer.

**Løkken.** Først sendes det, `Start()` returnerer, altså "Knock! Knock!". Så læses én linje ad gangen fra klienten, sendes til `ProcessInput()`, og svaret sendes tilbage. Løkken stopper, når protokollen siger `IsDone`, eller når `ReadLine()` returnerer `null`, hvilket betyder, at klienten har lukket forbindelsen.

**Fejlhåndtering.** Hele løkken står i en `try`/`catch`. Hvis klienten forsvinder midt i en samtale, eller serveren lukker forbindelsen ved Ctrl+C, bliver exceptionen fanget her, så den kun rammer den ene klient. En ubehandlet exception på en tråd ville ellers få hele serverprocessen til at stoppe. `finally` kører altid, uanset hvordan løkken sluttede, så forbindelsen bliver altid lukket, og ressourcerne bliver frigivet.

```csharp
private void Send(StreamWriter writer, string message)
{
    Console.WriteLine($"[Client {_id}] SERVER: {message}");
    writer.WriteLine(message);
}
```

Alt bliver også skrevet i serverens konsol med klientens nummer foran, så man kan se, at flere samtaler kører på samme tid.

---

## `KnockKnockProtocol.cs`

Selve samtalelogikken. Den kender intet til sockets, så man kunne i princippet teste den uden at oprette en TCP-forbindelse. Hver klient får sit eget objekt, så samtaletilstanden aldrig bliver delt mellem klienter.

```csharp
public class KnockKnockProtocol
{
    private readonly IJokeSelector _jokeSelector;

    public KnockKnockProtocol(IJokeSelector jokeSelector)
    {
        _jokeSelector = jokeSelector;
    }

    public IConversationState State { get; set; } = new WaitingForWhosThereState();
    public KnockKnockJoke CurrentJoke { get; private set; } = new KnockKnockJoke();
    public bool IsDone { get; set; }

    public string Start()
    {
        CurrentJoke = _jokeSelector.SelectJoke();
        State = new WaitingForWhosThereState();
        return "Knock! Knock!";
    }

    public string ProcessInput(string input)
    {
        return State.Handle(this, input);
    }

    public static bool Matches(string input, string expected)
    {
        return string.Equals(input.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
```

Den holder tre ting: den aktuelle tilstand (`State`), den aktuelle joke (`CurrentJoke`) og om samtalen er slut (`IsDone`).

- **`Start()`** henter en joke fra strategien, sætter tilstanden tilbage til start og returnerer "Knock! Knock!". Den bruges både når samtalen starter, og når klienten vil have en joke mere.
- **`ProcessInput()`** indeholder ingen `if` eller `switch`. Den sender bare beskeden videre til den aktuelle tilstand. Det er tilstanden, der bestemmer svaret og den næste tilstand. Det er kernen i State Pattern.
- **`Matches()`** sammenligner klientens svar med det forventede uden at tage hensyn til store og små bogstaver (`OrdinalIgnoreCase`) eller mellemrum i enderne (`Trim()`). Så "who's there?" og " Who's There? " bliver begge godkendt.

`CurrentJoke` har `private set`, så kun protokollen selv kan skifte joke.

---

## `Protocol/States/` (State Pattern)

I stedet for en enum og en stor `switch` er hver tilstand i samtalen sit eget objekt. Fordelen er, at hver tilstand kun indeholder sin egen logik, og at man kan tilføje en ny tilstand uden at ændre de eksisterende.

### `IConversationState.cs`

```csharp
public interface IConversationState
{
    string Handle(KnockKnockProtocol protocol, string input);
}
```

Hver tilstand får protokollen og klientens besked og returnerer serverens svar. Protokollen bliver sendt med, så tilstanden kan læse den aktuelle joke og skifte til en ny tilstand.

### `WaitingForWhosThereState.cs`

```csharp
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
```

Tilstanden lige efter "Knock! Knock!". Er svaret "Who's there?", skifter den til `WaitingForWhoState` og sender jokens `Who`, fx "Lettuce". Ved et forkert svar ændres `protocol.State` ikke, så klienten stadig er i samme tilstand, og serveren fortæller, hvad der skulle have stået.

### `WaitingForWhoState.cs`

```csharp
public string Handle(KnockKnockProtocol protocol, string input)
{
    string expected = protocol.CurrentJoke.Who + " who?";

    if (KnockKnockProtocol.Matches(input, expected))
    {
        protocol.State = new FinishedState();
        return protocol.CurrentJoke.Punchline + " Want another joke? (y/n)";
    }

    return "You are supposed to say: " + expected;
}
```

Det forventede svar bygges ud fra den aktuelle joke, fx "Lettuce who?". Er svaret rigtigt, skifter den til `FinishedState` og sender punchline'en. "Want another joke? (y/n)" står på samme linje, fordi protokollen er én besked pr. linje, og klienten kun læser én linje ad gangen. Ved et forkert svar bliver den i samme tilstand.

### `FinishedState.cs`

```csharp
public string Handle(KnockKnockProtocol protocol, string input)
{
    string answer = input.Trim().ToLower();

    if (answer == "y" || answer == "yes")
    {
        return protocol.Start();
    }

    if (answer == "n" || answer == "no")
    {
        protocol.IsDone = true;
        return "Bye!";
    }

    return "Please answer y or n. Want another joke?";
}
```

Jokens punchline er sendt, og serveren spørger, om klienten vil have en til.

- **"y"/"yes":** kalder `protocol.Start()`, som henter en ny joke og starter forfra uden at afbryde TCP-forbindelsen.
- **"n"/"no":** sætter `IsDone = true` og siger "Bye!". Så stopper løkken i `ClientHandler`, og forbindelsen lukkes.
- **Alt andet:** tilstanden er uændret, og klienten bliver bedt om at svare y eller n.

### Et eksempel på en samtale

| Tilstand før | Klient skriver | Server svarer | Tilstand efter |
|---|---|---|---|
| (start) | (forbinder) | Knock! Knock! | WaitingForWhosThere |
| WaitingForWhosThere | hello | You are supposed to say: Who's there? | WaitingForWhosThere |
| WaitingForWhosThere | Who's there? | Lettuce | WaitingForWho |
| WaitingForWho | Lettuce who? | Lettuce in, it's cold out here! Want another joke? (y/n) | Finished |
| Finished | n | Bye! | (slut) |

---

## `IJokeSelector`, `RandomJokeSelector`, `SequentialJokeSelector` (Strategy Pattern)

Strategy Pattern betyder, at man har flere udskiftelige måder at gøre den samme ting på, gemt bag et fælles interface. Den, der bruger strategien, ved ikke hvilken én den har fået.

### `IJokeSelector.cs`

```csharp
public interface IJokeSelector
{
    KnockKnockJoke SelectJoke();
}
```

Interfacet har kun én metode. `KnockKnockProtocol` kender kun interfacet, så den er ligeglad med, hvordan jokes bliver valgt. Der findes kun én instans af selectoren (singleton), og den deles af alle klienterne. Derfor skal alle implementeringer være trådsikre.

### `RandomJokeSelector.cs`

```csharp
public KnockKnockJoke SelectJoke()
{
    return _jokes[Random.Shared.Next(_jokes.Count)];
}
```

Vælger en tilfældig joke. `Random.Shared.Next(n)` giver et tilfældigt tal fra 0 til n-1, som bruges som index i listen. Jeg bruger `Random.Shared`, fordi den er trådsikker. Hvis jeg i stedet havde ét almindeligt `new Random()`-objekt, som flere tråde kalder på samtidig, kunne dets interne tilstand gå i stykker, så den begyndte at returnere 0 hver gang.

### `SequentialJokeSelector.cs`

```csharp
private readonly object _lock = new object();
private int _nextIndex;

public KnockKnockJoke SelectJoke()
{
    lock (_lock)
    {
        KnockKnockJoke joke = _jokes[_nextIndex];
        _nextIndex = (_nextIndex + 1) % _jokes.Count;
        return joke;
    }
}
```

Vælger jokes i rækkefølge. `% _jokes.Count` (modulo) gør, at indexet går tilbage til 0, når den har brugt den sidste joke, så den starter forfra.

`_nextIndex` deles af alle klienttrådene, og det er et klassisk sted for en race condition. Uden `lock` kunne det ske sådan her:

1. Tråd A læser `_nextIndex` = 2.
2. Tråd B læser `_nextIndex` = 2, før A har nået at opdatere den.
3. Begge får joke nr. 2, og begge sætter `_nextIndex` = 3.
4. Joke nr. 3 bliver aldrig brugt i den runde.

`lock (_lock)` sørger for, at kun én tråd ad gangen kan være inde i blokken. Kommer tråd B, mens A er derinde, venter B, til A er færdig. På den måde bliver læsning og opdatering af indexet én samlet handling.

---

## Klienten (`KnockKnockClient/Program.cs`)

```csharp
using TcpClient client = new TcpClient();
client.Connect("localhost", 6000);

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
```

En simpel konsolklient, der snakker samme linjebaserede protokol som serveren. Den læser en linje fra serveren, skriver den ud, beder brugeren om et svar og sender det tilbage. Den inderste løkke sørger for, at man ikke kan sende et tomt svar. Den stopper, når serveren siger "Bye!", når serveren er fuld, eller når serveren lukker forbindelsen, så `ReadLine()` returnerer `null`. `using` sørger for, at forbindelsen bliver lukket, når programmet slutter.

For at vise flere samtidige klienter starter jeg bare klienten i flere terminaler.

---

## Diagrammerne (`docs/`)

### `docs/klassediagram.puml`

UML-klassediagrammet i PlantUML. Det er delt op i fire pakker: netværk, samtalelogik, valg af joke og konfiguration. Diagrammet viser:

- at `KnockKnockTcpServer` opretter en `ClientHandler` og en tråd pr. klient,
- at hver `ClientHandler` har sin egen `KnockKnockProtocol` (komposition, den udfyldte rombe),
- at protokollen afhænger af interfacene `IJokeSelector` og `IConversationState` og ikke af konkrete klasser,
- at Strategy Pattern og State Pattern er interfaces med flere implementeringer (stiplede pile med hul trekant),
- at netværk og samtalelogik ligger i hver sin pakke.

### `docs/tilstandsdiagram.puml`

```
[*] --> WaitingForWhosThere : klient forbinder / "Knock! Knock!"
WaitingForWhosThere --> WaitingForWho : "Who's there?" / <Who>
WaitingForWhosThere --> WaitingForWhosThere : forkert svar
WaitingForWho --> Finished : "<Who> who?" / <Punchline> Want another joke?
WaitingForWho --> WaitingForWho : forkert svar
Finished --> WaitingForWhosThere : "y" / ny joke, "Knock! Knock!"
Finished --> Finished : andet svar
Finished --> [*] : "n" / "Bye!"
```

UML-tilstandsdiagrammet for én klients samtale. Det viser starttilstanden, de tre tilstande, hvilke beskeder der udløser et skift, og hvad serveren svarer. Forkerte svar er pile, der går tilbage til samme tilstand. Fra `Finished` går "y" tilbage til starten med en ny joke, og "n" afslutter samtalen. Diagrammet passer én til én med klasserne i `Protocol/States/`.

---

## Tråde og race conditions

En race condition opstår, når flere tråde læser og skriver de samme data på samme tid, og resultatet afhænger af, hvilken tråd der tilfældigvis kommer først. Derfor har jeg gennemgået, hvilke data der er pr. klient, og hvilke der er delte.

**Data, der kun hører til én klient** (kun brugt af klientens egen tråd, så ingen risiko):

- `TcpClient`, `StreamReader` og `StreamWriter`
- `ClientHandler`
- `KnockKnockProtocol` med `State`, `CurrentJoke` og `IsDone`

**Data, der deles mellem tråde:**

| Delt data | Hvem bruger den | Hvordan jeg håndterer det |
|---|---|---|
| `_clients`-listen i serveren | Hovedtråden tilføjer, klienttrådene fjerner, Ctrl+C-tråden lukker dem | `lock (_lock)` om al adgang |
| `_nextIndex` i `SequentialJokeSelector` | Alle klienttråde | `lock (_lock)` |
| `Random` i `RandomJokeSelector` | Alle klienttråde | `Random.Shared`, som er trådsikker |
| Listen med jokes | Alle klienttråde | Bliver kun læst, aldrig ændret, så det er sikkert |
| `clientId` i `Run()` | Kun hovedtråden | Ingen synkronisering nødvendig |
| `Console.WriteLine` | Alle tråde | Er trådsikker i .NET |

---

## SOLID

- **Single Responsibility:** Hver klasse har ét ansvar. Serveren accepterer forbindelser og starter tråde, `ClientHandler` snakker med én klient, protokollen og tilstandene kender reglerne, og selectorerne vælger jokes. Ingen klasse gør det hele, som det oprindelige `Program.cs` gjorde.
- **Open/Closed:** Koden er åben for udvidelse, men lukket for ændring. En ny strategi, fx en der aldrig gentager en joke, kræver en ny klasse, der implementerer `IJokeSelector`, og én linje i `Program.cs`. `KnockKnockProtocol`, tilstandene og netværkskoden skal ikke røres. Det samme gælder en ny tilstand i samtalen.
- **Liskov Substitution:** Både `RandomJokeSelector` og `SequentialJokeSelector` kan bruges alle steder, hvor der står `IJokeSelector`, uden at protokollen opfører sig anderledes. De returnerer altid en gyldig joke. Det samme gælder de tre tilstande og `IConversationState`.
- **Interface Segregation:** `IJokeSelector` og `IConversationState` har hver kun én metode, nemlig den, der faktisk bliver brugt. Ingen klasse er tvunget til at implementere metoder, den ikke har brug for.
- **Dependency Inversion:** Serveren og protokollen afhænger af abstraktionen `IJokeSelector` og ikke af en konkret strategi. Konfigurationen kommer ind gennem konstruktøren som færdige objekter (`ServerOptions`) i stedet for at blive læst direkte i klasserne. Det er `Program.cs`, der bestemmer, hvilke konkrete klasser der bruges.

---

## Mine valg, og hvad jeg kunne have gjort i stedet

Næsten alt i løsningen kunne være lavet på en anden måde. Her gennemgår jeg de vigtigste valg: hvad jeg gjorde, hvorfor, hvilke alternativer der findes, og hvornår alternativet ville være bedre. Den gennemgående begrundelse er, at opgaven handler om at vise tråde, race conditions, design patterns og konfiguration. Derfor har jeg valgt de løsninger, hvor de ting er tydelige i koden, også selvom en "produktionsserver" ville gøre nogle af dem anderledes.

### 1. Én `Thread` pr. klient

**Det gjorde jeg:** Hovedtråden accepterer forbindelser, og hver klient får sin egen `new Thread(...)`, der kører en blokerende løkke med `ReadLine()`.

**Hvorfor:** Det er den mest direkte måde at vise multithreading på. Man kan pege på præcis den linje, hvor en ny tråd starter, og koden læses fra top til bund som en almindelig samtale. Med et lille, fast antal klienter (`MaxClients`) er det ikke et problem, at hver tråd koster ressourcer.

**Alternativer:**

- **`async`/`await` med `AcceptTcpClientAsync` og `ReadLineAsync`.** Mens en klient tænker over sit svar, bruger den ingen tråd. Tråden gives tilbage til thread pool'en og bruges til andre klienter. Det skalerer til tusindvis af klienter, hvor én tråd pr. klient hurtigt bruger meget hukommelse (hver tråd reserverer typisk 1 MB stack).
  ```csharp
  TcpClient client = await _listener.AcceptTcpClientAsync(token);
  _ = HandleClientAsync(client, token); // ingen ny tråd
  ```
- **`ThreadPool.QueueUserWorkItem` eller `Task.Run`.** Genbruger tråde i stedet for at oprette nye. Men min `ReadLine()` blokerer, og blokerende kode i thread pool'en kan sulte den, så det ville faktisk være dårligere end egne tråde, medmindre koden også blev gjort asynkron.

**Hvornår ville jeg vælge anderledes:** Hvis serveren skulle håndtere mange samtidige klienter, ville jeg gå over til `async`/`await`. Det er den måde, man normalt laver netværksservere på i .NET i dag.

### 2. `lock` til de delte data

**Det gjorde jeg:** `lock (_lock)` om `_clients`-listen og om `_nextIndex` i `SequentialJokeSelector`.

**Hvorfor:** Begge steder skal flere operationer ske som én samlet handling. I serveren er det "tjek antal, og tilføj så", og i selectoren er det "læs index, og opdater det". `lock` løser begge dele, er nemt at læse og er det, vi har lært om race conditions.

**Alternativer:**

- **`Interlocked.Increment`** i `SequentialJokeSelector`. Den laver læs-og-opdater som én atomar CPU-operation uden en lås:
  ```csharp
  int i = Interlocked.Increment(ref _counter) - 1;
  return _jokes[i % _jokes.Count];
  ```
  Det er hurtigere, men man skal tænke over, at tælleren til sidst løber over `int.MaxValue` og bliver negativ.
- **`ConcurrentDictionary`/`ConcurrentBag`** til klientlisten. De er trådsikre i sig selv, men løser ikke mit "tjek og tilføj"-problem, fordi `Count` og `Add` stadig er to separate kald. Derfor ville jeg stadig skulle bruge en lås.
- **`SemaphoreSlim(MaxClients)`** til at begrænse antallet af klienter. Det er præcis det, en semaphore er lavet til: "højst N ad gangen". Med `Wait(0)` kan man afvise med det samme, og med `Wait()` kan man lade klienten stå i kø i stedet for at afvise den.

**Hvornår ville jeg vælge anderledes:** Hvis låsen blev en flaskehals ved meget høj belastning. Med få klienter og en lås, der holdes i få mikrosekunder, er `lock` det klareste valg.

### 3. `Random.Shared`

**Det gjorde jeg:** `Random.Shared.Next(...)` i `RandomJokeSelector`.

**Hvorfor:** Selectoren er en singleton og bliver kaldt fra flere tråde samtidig. Et almindeligt `new Random()` er ikke trådsikkert. `Random.Shared` (fra .NET 6) er trådsikker og kræver ingen ekstra kode.

**Alternativer:** En `lock` om et almindeligt `Random`-objekt, eller `ThreadLocal<Random>`, så hver tråd har sit eget. Begge virker, men er mere kode for det samme resultat. Før .NET 6 ville det have været de eneste muligheder.

### 4. Linjebaseret tekstprotokol

**Det gjorde jeg:** Én besked pr. linje med `StreamWriter.WriteLine` og `StreamReader.ReadLine`.

**Hvorfor:** TCP er en strøm af bytes og kender ikke til "beskeder". Jeg skal selv bestemme, hvor én besked slutter. Linjeskift er den simpleste løsning, når beskederne er korte tekststrenge uden linjeskift i sig selv. Protokollen kan læses af mennesker, og man kan teste serveren med fx `telnet localhost 6000` helt uden min klient.

**Alternativer:**

- **Længde-præfiks:** Send først antallet af bytes (fx 4 bytes), og derefter selve beskeden. Det er nødvendigt, hvis beskederne kan indeholde linjeskift eller er binære data.
- **JSON pr. linje:** Fx `{"type":"punchline","text":"...","askAgain":true}`. Så kunne klienten se, *hvilken slags* besked den har fået, i stedet for at sammenligne med teksten `"Bye!"`, som den gør nu.
- **Rå `stream.Read`:** Det ville give problemer, fordi to beskeder kan komme i samme læsning, eller én besked kan blive delt over flere.

**Hvornår ville jeg vælge anderledes:** Hvis protokollen skulle udvides med flere slags beskeder. En svaghed ved min løsning er, at klienten er afhængig af de præcise strenge `"Bye!"` og `"Server is busy..."`. Ændrer man teksten på serveren, går klienten i stykker.

### 5. TCP

**Hvorfor:** En Knock Knock-samtale har en tilstand, og beskederne skal komme frem i den rigtige rækkefølge og uden at forsvinde. TCP garanterer begge dele og holder forbindelsen åben, så serveren ved, hvilken klient der skriver.

**Alternativer:** UDP er hurtigere, men beskeder kan forsvinde eller komme i forkert rækkefølge, og der er ingen forbindelse at knytte samtalen til. HTTP/REST er forbindelsesløst, så serveren skulle selv holde styr på samtalen med fx et session-id. WebSockets eller SignalR ville passe godt, hvis klienten var en browser.

### 6. State Pattern i stedet for `enum` og `switch`

**Det gjorde jeg:** Én klasse pr. tilstand bag `IConversationState`.

**Hvorfor:** Hver tilstand indeholder kun sin egen logik, og en ny tilstand kræver en ny klasse i stedet for at ændre en stor `switch`. Det passer én til én med tilstandsdiagrammet.

**Alternativet:**
```csharp
switch (_state)
{
    case State.WaitingForWhosThere: ...
    case State.WaitingForWho: ...
    case State.Finished: ...
}
```
**Ærligt:** Med kun tre tilstande ville en `switch` være kortere og lige så let at læse. State Pattern betaler sig først, når der kommer flere tilstande, eller når hver tilstand får meget logik. Jeg har valgt det, fordi det gør koden åben for udvidelse (Open/Closed), og fordi det er et af de patterns, opgaven skulle vise.

**Ting jeg kunne gøre anderledes inden for State Pattern:**

- Mine tilstande har ingen felter, så jeg behøver ikke oprette et nyt objekt ved hvert skift. De kunne være `static readonly`-instanser, som genbruges.
- Tilstanden sætter selv `protocol.State`. Alternativt kunne `Handle` returnere den næste tilstand sammen med svaret, så tilstandene ikke behøver at ændre i protokollen. Så kunne `State` have en `private set`.

### 7. Strategy Pattern og valget i `Program.cs`

**Det gjorde jeg:** `IJokeSelector` med to implementeringer, og en `if` i `Program.cs`, der vælger ud fra konfigurationen.

**Hvorfor:** Protokollen skal ikke vide, hvordan jokes vælges. Valget sker ét sted, og en ny strategi kræver kun en ny klasse og én linje i `Program.cs`.

**Alternativer:**

- **En `if` direkte i `KnockKnockProtocol`.** Kortere, men så ville protokollen skulle ændres hver gang, der kom en ny strategi.
- **Keyed services** (fra .NET 8): Med en DI-container kunne man registrere `AddKeyedSingleton<IJokeSelector, RandomJokeSelector>("Random")` og så slå op med strategiens navn. Så forsvinder `if`'en i `Program.cs` også.
- **En delegate** som `Func<KnockKnockJoke>` i stedet for et interface. Det virker til så lille et interface med én metode, men et interface er tydeligere og nemmere at vise i et klassediagram.

### 8. Singleton-selector, men en ny protokol pr. klient

**Hvorfor:** Det er den vigtigste skelnen i hele designet. Protokollen indeholder samtaletilstanden, som *ikke* må deles, så hver klient får sin egen. Selectoren skal deles, ellers ville `Sequential` starte forfra for hver klient.

**Alternativ:** Jeg opretter protokollen med `new` i serveren. Med en DI-container kunne man også registrere `KnockKnockProtocol` som `Transient` og lade serveren få en `Func<KnockKnockProtocol>` (en factory). Det ville gøre serveren uafhængig af, hvordan protokollen bygges, men er mere indviklet, end opgaven kræver.

### 9. Options-klasser med `.Get<T>()` og egen validering

**Det gjorde jeg:** Hver sektion i `appsettings.json` bliver læst ind i sin egen klasse med `.Get<T>()`, og bagefter tjekker jeg værdierne med almindelige `if`-sætninger i `Program.cs`.

**Hvorfor:** Stærkt typede klasser i stedet for tekstnøgler som `config["Server:Port"]`. Valideringen sikrer, at serveren stopper med en forståelig besked i stedet for at fejle senere på en uforståelig måde. Med almindelige `if`-sætninger kan man læse valideringen fra top til bund uden at kende til frameworket.

**Alternativer:**

- **`config["Server:Port"]`** direkte. Mindre kode, men ingen typer, og en stavefejl i nøglen giver bare `null`.
- **`System.Text.Json` direkte** til at læse filen ind i en klasse. Virker fint, men så mister man muligheden for at overskrive værdier fra kommandolinjen.
- **Microsofts Options Pattern** med `AddOptions<T>().Bind(...).Validate(...)` og `IOptions<T>`. Så står reglerne ved registreringen, og de kan vise flere fejl på én gang. Men det kræver en DI-container, og det er sværere at se, hvornår valideringen faktisk bliver kørt.
- **Data annotations** som `[Range(1, 65535)]` på properties. Reglerne står så ved selve propertien i stedet for i `Program.cs`.
- **`Strategy` som en `enum`** i stedet for en `string`. Så ville binderen selv afvise ugyldige værdier, og man ville slippe for at sammenligne med tekststrenge.

### 10. Ingen DI-container og ingen kørende host

**Det gjorde jeg:** Jeg bruger kun `Host.CreateApplicationBuilder` til at læse konfigurationen. Alle objekter opretter jeg selv med `new` i `Program.cs`, og jeg starter selv serveren med `server.Run()` og håndterer Ctrl+C selv.

**Hvorfor:** Programmet har kun fire objekter at oprette, så en DI-container ville være mere indviklet end at skrive `new`. Det er stadig dependency injection, bare gjort i hånden: klasserne får deres afhængigheder gennem konstruktøren og opretter dem ikke selv. Trådene og nedlukningen er synlige i min egen kode, i stedet for at være gemt i frameworket.

**Alternativ:** Registrere klasserne i `builder.Services` (fx `AddSingleton<IJokeSelector>(...)`), gøre serveren til en `BackgroundService` og kalde `host.Run()`. Så håndterer hosten Ctrl+C, logging og validering automatisk, og serveren får et `CancellationToken`, når den skal stoppe. Det er den måde, man normalt laver en større, langtkørende service på i .NET.

### 11. Nedlukning ved at lukke sockets

**Det gjorde jeg:** `Stop()` lukker listeneren og alle klientforbindelser. Det får `AcceptTcpClient()` og `ReadLine()` til at kaste en exception, som jeg bruger som signal til at stoppe.

**Hvorfor:** Med blokerende kald er det den eneste måde at "vække" en tråd, der står og venter.

**Alternativ:** Med `async` kunne jeg bruge et `CancellationToken`: `AcceptTcpClientAsync(token)` og `ReadLineAsync(token)` stopper pænt, når tokenet annulleres. Det er renere end at bruge exceptions til normal kontrolflow.

### 12. Afvisning, når serveren er fuld

**Hvorfor:** Klienten får en klar besked med det samme i stedet for at hænge og vente.

**Alternativ:** Lade klienten vente i kø, til der bliver plads, fx med `SemaphoreSlim.Wait()`. Det ville give en bedre oplevelse ved korte spidsbelastninger, men klienten ville ikke vide, hvorfor den venter.

### 13. `IPAddress.Loopback`

**Hvorfor:** Serveren kan kun nås fra min egen computer. Det er sikrest under udvikling.

**Alternativ:** `IPAddress.Any`, så andre computere på netværket kan forbinde. Det kunne være en indstilling i `appsettings.json`.

### Svagheder jeg selv kender

Det er godt at nævne dem selv, før censor gør det:

- Klienten har `"localhost"` og `6000` skrevet direkte i koden. Ændrer man porten i serverens konfiguration, skal klienten også rettes. Det kunne komme fra `args` eller klientens egen konfiguration.
- Klienten genkender slutningen ved at sammenligne med de præcise tekster fra serveren (se punkt 4).
- Der er ingen unit tests, selvom protokollen er lavet, så den kan testes uden netværk. En test kunne fx oprette `KnockKnockProtocol` med en selector, der altid returnerer samme joke, og tjekke svarene.
- Der er ingen timeout. En klient, der forbinder og aldrig skriver noget, optager en plads for evigt. Man kunne sætte `client.ReceiveTimeout`.

---

## Sådan demonstrerer jeg det

| Test | Sådan gør jeg | Forventet resultat |
|---|---|---|
| Én klient forbinder | Start serveren og én klient | Serveren sender "Knock! Knock!" |
| Tre klienter samtidig | Start klienten i tre terminaler | Alle gennemfører deres egen samtale, serverloggen viser `[Client 1]`, `[Client 2]`, `[Client 3]` blandet |
| Forkert svar | Skriv noget forkert | "You are supposed to say: ..." og samme tilstand |
| En klient afbryder | Luk ét klientvindue midt i en joke | Serveren skriver "Disconnected", de andre fortsætter |
| Random Strategy | `"Strategy": "Random"` | Tilfældige jokes |
| Sequential Strategy | `"Strategy": "Sequential"` | Jokes kommer i rækkefølge, også på tværs af klienter |
| Ny joke | Tilføj en joke i JSON og genstart | Den nye joke kan komme frem |
| Maks klienter | Sæt `"MaxClients": 2` og start tre klienter | Den tredje får "Server is busy. Please try again later." |
| Ugyldig konfiguration | Sæt `"Port": 0` eller `"Strategy": "Foo"` | Forståelig fejlbesked, serveren starter ikke |
| Serveren stoppes | Tryk Ctrl+C i serverens vindue | "Stopping server...", alle klienter bliver afbrudt, "Server stopped" |

Bemærk: Kører man serveren fra Visual Studio, læser den `appsettings.json` i `bin`-mappen. Ændrer man filen i projektet, skal man bygge igen, så den bliver kopieret over. Konfigurationen kan også overskrives fra kommandolinjen uden at røre filen, fx `dotnet run -- --JokeSelection:Strategy=Sequential`.
