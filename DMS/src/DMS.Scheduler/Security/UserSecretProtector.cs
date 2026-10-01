using System;
using System.Security.Cryptography;
using System.Text;

namespace DMS.Scheduler.Security;

public static class UserSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DMS.Scheduler.v9");

    public static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var clear = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return string.Empty;
        var bytes = Convert.FromBase64String(protectedValue);
        var clear = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clear);
    }
}
