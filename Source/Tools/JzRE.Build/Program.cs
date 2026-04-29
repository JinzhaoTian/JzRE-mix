using JzRE.Build;

try
{
    var opts    = CommandLine.Parse(args);
    var builder = new Builder(opts);

    Console.WriteLine($"JzRE.Build v0.1 | Target: {opts.Target} | Config: {opts.Configuration} | Platform: {opts.Platform}");

    if (opts.GenerateProjectFiles)
        builder.GenerateProjectFiles();
    else if (opts.BuildBindingsOnly)
        builder.BuildBindingsOnly();
    else
        builder.Build();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Build error: {ex.Message}");
    Console.ResetColor();
    Environment.Exit(1);
}
