using Quantum.CodeGen;

// usage: Qtnc <output dir> <file.qtn>...
var outDir = args[0];
Directory.CreateDirectory(outDir);
var warnings = 0;
var files = Generator.Generate(args.Skip(1).ToArray(), new GeneratorOptions(), w =>
{
    warnings++;
    Console.WriteLine($"warning {w.Path}({w.Position}): {w.Message}");
});
foreach (var f in files)
{
    File.WriteAllText(Path.Combine(outDir, Path.GetFileName(f.Name)), string.Join("\n", f.Contents));
    Console.WriteLine($"generated {f.Name}");
}
Console.WriteLine($"DSL compiled with {warnings} warning(s)");
