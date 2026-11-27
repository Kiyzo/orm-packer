using System.Globalization;
using System.Windows.Forms;

namespace OrmPacker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (Cli.IsVersionRequest(args))
        {
            Cli.AttachParentConsole();
            Console.WriteLine(Cli.VersionText());
            return;
        }

        Localization.Initialize(CultureInfo.CurrentUICulture);
        // An unknown switch is a typo, not a texture file: show usage instead of a GUI error.
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal) && !Cli.IsCommand(args))
        {
            Cli.AttachParentConsole();
            Environment.ExitCode = Cli.Run(new[] { "unknown" }, Console.Out);
            return;
        }
        if (Cli.IsCommand(args))
        {
            Cli.AttachParentConsole();
            Environment.ExitCode = Cli.Run(args, Console.Out);
            return;
        }
        ApplicationConfiguration.Initialize();

        // No failure should silently close the application without a message.
        Application.ThreadException += (_, e) => ShowCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception) ShowCrash(exception);
        };

        try
        {
            Application.Run(new MainForm(args));
        }
        catch (Exception ex)
        {
            ShowCrash(ex);
        }
    }

    private static void ShowCrash(Exception exception)
    {
        WriteCrashLog(exception);
        MessageBox.Show(
            $"{exception.GetType().Name}: {exception.Message}",
            Localization.Get("msg.errorTitle"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrmPacker");
            Directory.CreateDirectory(directory);
            var entry = $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, "crash.log"), entry);
        }
        catch (Exception)
        {
            // Logging must never mask the original failure.
        }
    }
}
