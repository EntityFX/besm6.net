namespace Besm6.BitVisualizer.WinForms;

internal static class Program
{
    /// <summary>Точка входа приложения.</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}