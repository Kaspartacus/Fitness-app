# Data and local state

Protected data is scoped to an owner derived from validated Server identity, never a client-submitted user ID or role. Preserve recorded history when plans, goals, definitions, or catalog entries change. Infrastructure owns EF Core entities and SQLite migrations; integration tests use isolated real SQLite databases.

Local SQLite and Data Protection paths may resolve relative to the Server content root. A different checkout can therefore show separate accounts and catalog data. Ordinary development uses `Desktop/Fitness app/Fitness-app`; inspect ignored data and active processes before any checkout cleanup. Keep databases and keys outside `wwwroot` and Git. Read current models, migrations, and `README.md` for exact storage behavior.
