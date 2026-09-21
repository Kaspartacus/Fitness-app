# Contracts guidance

This project owns concrete transport DTOs shared by the Client and Server. It has no project references.

- Keep contracts serialization-friendly and free of EF entities, persistence behavior, authorization decisions, and UI concerns.
- Coordinate a contract change with its Client callers, Server endpoint mapping, and integration tests in the same slice.
- Do not turn Contracts into a second domain or application layer.
- Check [project facts](../../docs/project.md) for API-relevant invariants. `$fitness-feature` covers a coordinated change.
