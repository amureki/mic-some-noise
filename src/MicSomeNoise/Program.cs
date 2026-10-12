using System;
using System.Threading;
using System.Windows;

namespace MicSomeNoise;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var instance = new Mutex(true, "MicSomeNoise.SingleInstance", out bool first);
        if (!first) { MessageBox.Show("Mic Some Noise is already running. Open it from the system tray."); return; }
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Run(new MainWindow());
        }
        catch (Exception e) { MessageBox.Show(e.ToString(), "Mic Some Noise could not start"); }
    }
}
