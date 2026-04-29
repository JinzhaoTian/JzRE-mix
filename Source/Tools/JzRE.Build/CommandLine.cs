namespace JzRE.Build;

/// <summary>
/// The target configuration modes. Mirrors FlaxEngine's TargetConfiguration.
/// </summary>
public enum TargetConfiguration
{
    /// <summary>Debug configuration. Without optimizations but with full debugging information.</summary>
    Debug = 0,

    /// <summary>Develop configuration. With basic optimizations and partial debugging data.</summary>
    Develop = 1,

    /// <summary>Shipping configuration. With full optimization and no debugging data.</summary>
    Release = 2,
}

public class BuildOptions
{
    public string               Target               = "Editor";
    public TargetConfiguration  Configuration        = TargetConfiguration.Debug;
    public string               Platform             = "Windows";
    public bool                 GenerateProjectFiles = false;
    public bool                 BuildBindingsOnly    = false;
    public bool                 Verbose              = false;
    public string               WorkspaceDir         = Directory.GetCurrentDirectory();

    /// <summary>Enable scripting debugger (sets --debug flag on editor launch).</summary>
    public bool                 Debug                = false;
    /// <summary>Suspend startup until a debugger attaches.</summary>
    public bool                 WaitForDebugger      = false;
    /// <summary>Debugger agent address (default 127.0.0.1:41000+pid%1000).</summary>
    public string               DebuggerAddress      = "";

    /// <summary>
    /// Override project format (null = use platform default).
    /// Set via -vscode or -vs2022 CLI flags. Mirrors FlaxEngine's
    /// Configuration.ProjectFormat* pattern.
    /// </summary>
    public ProjectFormat? ProjectFormat = null;
}

public static class CommandLine
{
    public static BuildOptions Parse(string[] args)
    {
        var opts = new BuildOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--generate-project-files":
                case "-genproject":
                    opts.GenerateProjectFiles = true;
                    break;

                case "-BuildBindingsOnly":
                    opts.BuildBindingsOnly = true;
                    break;

                case "--verbose": case "-v":
                    opts.Verbose = true;
                    break;

                case "-vscode":
                    opts.ProjectFormat = ProjectFormat.VisualStudioCode;
                    break;

                case "-vs2022":
                    opts.ProjectFormat = ProjectFormat.VisualStudio2022;
                    break;

                case "--debug":
                case "-debug":
                    opts.Debug = true;
                    break;

                case "--debugwait":
                case "-debugwait":
                    opts.WaitForDebugger = true;
                    break;

                // Space-separated forms: --target X, --config X, ...
                case "--target":    if (i + 1 < args.Length) opts.Target        = args[++i]; break;
                case "--config":    if (i + 1 < args.Length) opts.Configuration = ParseConfig(args[++i]); break;
                case "--platform":  if (i + 1 < args.Length) opts.Platform      = args[++i]; break;
                case "--workspace": if (i + 1 < args.Length) opts.WorkspaceDir  = args[++i]; break;

                case "--debugger-address": if (i + 1 < args.Length) opts.DebuggerAddress = args[++i]; break;

                default:
                    // --key=value forms
                    if (arg.StartsWith("--target="))         opts.Target        = arg[9..];
                    else if (arg.StartsWith("--config="))    opts.Configuration = ParseConfig(arg[9..]);
                    else if (arg.StartsWith("--platform="))  opts.Platform      = arg[11..];
                    else if (arg.StartsWith("--workspace=")) opts.WorkspaceDir  = arg[12..];
                    else if (arg.StartsWith("--debugger-address=")) opts.DebuggerAddress = arg[19..];
                    break;
            }
        }
        return opts;
    }

    private static TargetConfiguration ParseConfig(string value) => value switch
    {
        "Debug"       => TargetConfiguration.Debug,
        "Develop"     => TargetConfiguration.Develop,
        "Release"     => TargetConfiguration.Release,
        _             => throw new ArgumentException($"Unknown configuration: '{value}'. Valid: Debug, Develop, Release.")
    };
}
