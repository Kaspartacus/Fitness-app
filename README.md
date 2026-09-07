# FitnessApp

FitnessApp is a private, mobile-first fitness application in an intentionally public source repository. The source is publicly inspectable; protected functionality and personal data will require authentication and administrator-approved access. The repository currently contains only the technical foundation: a Blazor WebAssembly client hosted by an ASP.NET Core server and three empty architectural layers.

Planned product behavior and the distinction between implemented and deferred scope are documented in [docs/project.md](docs/project.md). Current evidence and blockers are tracked in [docs/progress.md](docs/progress.md).

## Prerequisites

- .NET SDK 10.0.103 or a later compatible .NET 10 feature-band SDK, as controlled by `global.json`
- A trusted ASP.NET Core development certificate only when using the optional HTTPS launch profile

No Node.js runtime, database, container engine, or cloud account is required for the current foundation.

## Repository structure

The solution file is `FitnessApp.slnx`.

```text
src/
  FitnessApp.Client          Blazor WebAssembly user interface
  FitnessApp.Server          ASP.NET Core host, future API, composition root
  FitnessApp.Application     Application use cases
  FitnessApp.Domain          Business rules and domain types
  FitnessApp.Infrastructure  Persistence and external integrations
```

## Run locally

From the repository root:

```bash
dotnet restore FitnessApp.slnx
dotnet run --project src/FitnessApp.Server --launch-profile http
```

Open <http://localhost:5192>. The server serves the WebAssembly client and is the single origin for the future API.

To build without starting the app:

```bash
dotnet build FitnessApp.slnx --no-restore
```
