using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;

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

    // Every value from the unannotated library is handled where it arrives, so nothing here is reported.
    private static void AddUser(PrincipalContext context, string groupName, string userName)
    {
        // FindByIdentity returns null when nothing matches.
        if (GroupPrincipal.FindByIdentity(context, groupName) is not { } group)
        {
            Console.WriteLine($"No group named '{groupName}'.");
            return;
        }

        var user =
            UserPrincipal.FindByIdentity(context, userName)
            ?? throw new InvalidOperationException($"No user named '{userName}'.");

        // Reviewed: Members is never null for a group that was found.
        group.Members!.Add(user);
        group.Save();
    }

    // Not handled yet: each of these is reported until it is.
    private static void PrintMembers(PrincipalContext context, string groupName)
    {
        var group = GroupPrincipal.FindByIdentity(context, groupName);

        foreach (Principal member in group.Members)
        {
            Console.WriteLine(member.Name);
        }

        // A callback whose delegate type comes from unannotated code.
        LegacyDirectory.Visit(group, member => Console.WriteLine(member.DistinguishedName.Length));

        // List<Principal> declares the elements non-null, so later reads of them are not reported.
        List<Principal> members = [.. group.Members];
        Console.WriteLine(members.Count);
    }
}
