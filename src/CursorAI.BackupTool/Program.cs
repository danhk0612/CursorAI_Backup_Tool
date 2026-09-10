namespace CursorAI.BackupTool;

internal static class Program
{
    private const string MutexName = "Local\\CursorAI.BackupTool.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("CursorAI Backup Tool이 이미 실행 중입니다.", "이미 실행 중", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
