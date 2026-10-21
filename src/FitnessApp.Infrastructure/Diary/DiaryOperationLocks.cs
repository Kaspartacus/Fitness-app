using System.Collections.Concurrent;

namespace FitnessApp.Infrastructure.Diary;

// The application has one server. All code checks, grant checks and content changes for an
// account share a gate; persistent counters still survive process restarts/session changes.
public sealed class DiaryOperationLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    public SemaphoreSlim For(string owner) => gates.GetOrAdd(owner, _ => new(1, 1));
}
