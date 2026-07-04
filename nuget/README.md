# Orkeon

Orkeon is a C# library for building and managing collaborative AI agent teams. It is a port of the Python Orkeon library, designed for .NET with Clean Architecture principles.

## Installation

```
dotnet add package Orkeon.Domain
dotnet add package Orkeon.Application
dotnet add package Orkeon.Infrastructure
```

## Quick Example

```csharp
using Orkeon.Domain.Builders;

var agent = new AgentBuilder()
    .Role("Researcher")
    .Goal("Find and summarize information on a given topic")
    .Verbose()
    .Build();

var crew = new CrewBuilder()
    .Goal("Produce a research report")
    .Sequential()
    .WithAgent(a => a.Role("Researcher").Goal("Gather data"))
    .WithTask(t => t.Description("Search and summarize the topic").ExpectedOutput("A concise report"))
    .Build();
```

## Documentation

Full documentation and examples are available on GitHub: https://github.com/Orkeon/orkeon
