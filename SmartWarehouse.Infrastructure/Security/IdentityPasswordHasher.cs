using Microsoft.AspNetCore.Identity;
using SmartWarehouse.Domain.Entities;

namespace SmartWarehouse.Infrastructure.Security;

/// <summary>Хеширование паролей стандартным механизмом ASP.NET Core Identity (PBKDF2, соль, итерации).</summary>
public class IdentityPasswordHasher : SmartWarehouse.Application.Interfaces.IPasswordHasher
{
    private static readonly PasswordHasher<UserAccount> Hasher = new();
    private static readonly UserAccount Dummy = new();

    public string Hash(string password) => Hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password) =>
        !string.IsNullOrEmpty(hash)
        && Hasher.VerifyHashedPassword(Dummy, hash, password) != PasswordVerificationResult.Failed;
}
