namespace JzRE.Build;

public class BuildOptions
{
    public string Target               = "Editor";
    public string Configuration        = "Debug";
    public string Platform             = "Windows";
    public bool   GenerateProjectFiles = false;
    public bool   Verbose              = false;
    public string WorkspaceDir         = Directory.GetCurrentDirectory();
}

public static class CommandLine
{
    public static BuildOptions Parse(string[] args)
    {
        var opts = new BuildOptions();
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--generate-project-files": opts.GenerateProjectFiles = true; break;
                case "--verbose": case "-v":     opts.Verbose = true; break;
                default:
                    if (arg.StartsWith("--target="))     opts.Target        = arg[9..];
                    else if (arg.StartsWith("--config=")) opts.Configuration = arg[9..];
                    else if (arg.StartsWith("--platform=")) opts.Platform    = arg[11..];
                    else if (arg.StartsWith("--workspace=")) opts.WorkspaceDir = arg[12..];
                    break;
            }
        }
        return opts;
    }
}
