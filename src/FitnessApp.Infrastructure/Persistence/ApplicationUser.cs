using FitnessApp.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace FitnessApp.Infrastructure.Persistence;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public AccountApprovalStatus ApprovalStatus { get; set; } = AccountApprovalStatus.Pending;

    public ICollection<UserSession> Sessions { get; } = [];
}
