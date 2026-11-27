namespace SftpEsitle;

/// <summary>Sürüm tek yerden gelir: SftpEsitle.csproj içindeki &lt;Version&gt;.</summary>
static class AppInfo
{
    public static readonly string Version =
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion.Split('+')[0]
        ?? "?";
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--make-icon")
        {
            Logo.SaveIco(args[1]);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(Theme.Dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        ToolStripManager.Renderer = new FluentMenuRenderer();

        if (args.Length == 1 && args[0] == "--uninstall")
        {
            Installer.Uninstall();
            return;
        }

        Application.Run(new MainForm());
    }
}
