#nullable disable
using System.DirectoryServices.AccountManagement;

namespace DirectoryServicesSample;

/// <summary>Stands in for an old helper library that was never annotated.</summary>
public delegate void MemberVisitor(Principal member);

public static class LegacyDirectory
{
    public static void Visit(GroupPrincipal group, MemberVisitor visitor)
    {
        foreach (Principal member in group.Members)
            visitor(member);
    }
}
