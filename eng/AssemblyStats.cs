// Usage: dotnet run eng/AssemblyStats.cs -- <path-to-dll>
// Prints one JSON object: file size, type/method counts, total IL bytes, using System.Reflection.Metadata.
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 1) { Console.Error.WriteLine("usage: AssemblyStats <dll>"); return 1; }

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();

int types = 0, methods = 0, ilBytes = 0, bodies = 0;
foreach (var handle in md.TypeDefinitions)
{
    var type = md.GetTypeDefinition(handle);
    if (md.GetString(type.Name) == "<Module>") continue;
    types++;
    foreach (var mh in type.GetMethods())
    {
        methods++;
        var m = md.GetMethodDefinition(mh);
        if (m.RelativeVirtualAddress == 0) continue;
        bodies++;
        ilBytes += pe.GetMethodBody(m.RelativeVirtualAddress).Size;
    }
}

Console.WriteLine($"{{\"dllBytes\":{new FileInfo(args[0]).Length},\"types\":{types},\"methods\":{methods},\"methodBodies\":{bodies},\"methodBodyBytes\":{ilBytes}}}");
return 0;
