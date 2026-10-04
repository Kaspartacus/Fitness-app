# Architecture

`FitnessApp.slnx` is the only solution. This is one .NET 10 modular monolith. `FitnessApp.Server` is the single ASP.NET Core host and HTTP boundary; it serves the Blazor WebAssembly Client's static assets. `FitnessApp.Client` calls the Server through same-origin HTTP and shares transport DTOs with it through `FitnessApp.Contracts`.

`FitnessApp.Application` owns use-case service contracts and models and depends on `FitnessApp.Domain`. `FitnessApp.Infrastructure` implements those contracts with EF Core, Identity, SQLite, and external adapters, depending on Application and Domain. Domain and Contracts have no solution-project references. The Server composes services; its Client project reference supports static hosting, not server-side UI logic.

Extend these existing projects for new features. Do not create a feature-specific solution, second host, or parallel application. Confirm exact references in project files and service registration in `src/FitnessApp.Infrastructure/DependencyInjection.cs`.
