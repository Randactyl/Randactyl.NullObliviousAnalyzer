using System;
using System.DirectoryServices.AccountManagement;

namespace DirectoryServicesSample;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("usage: DirectoryServicesSample <domain> <group> <user>");
            return;
        }

        using var context = new PrincipalContext(ContextType.Domain, args[0]);
        AddUser(context, args[1], args[2]);
        PrintMembers(context, args[1]);
    }

    private static void AddUser(PrincipalContext context, string groupName, string userName)
    {
        // Both lookups return null when nothing matches, and the compiler does not warn.
        GroupPrincipal group = GroupPrincipal.FindByIdentity(context, groupName);
        UserPrincipal user = UserPrincipal.FindByIdentity(context, userName);

        group.Members.Add(user);
        group.Save();
    }

    private static void PrintMembers(PrincipalContext context, string groupName)
    {
        var group = GroupPrincipal.FindByIdentity(context, groupName);

        foreach (Principal member in group.Members)
            Console.WriteLine(member.Name);

        // A callback whose delegate type comes from unannotated code.
        LegacyDirectory.Visit(group, member => Console.WriteLine(member.DistinguishedName));
    }
}
