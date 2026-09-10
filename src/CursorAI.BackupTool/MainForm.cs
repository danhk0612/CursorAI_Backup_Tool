using System.Diagnostics;

namespace CursorAI.BackupTool;

internal sealed class MainForm : Form
{
    private readonly BackupService _service = new();
    private readonly DataGridView _grid = new();
    private readonly RadioButton _normalRadio = new() { Text = "일반 백업", Checked = true, AutoSize = true };
    private readonly RadioButton _fullRadio = new() { Text = "전체 AI 백업 (workspaceStorage 포함)", AutoSize = true };
    private readonly Button _backupButton = new() { Text = "백업", Width = 100 };
    private readonly Button _restoreButton = new() { Text = "복원", Width = 100 };
    private readonly Button _deleteButton = new() { Text = "삭제", Width = 100 };
    private readonly Button _refreshButton = new() { Text = "새로 고침", Width = 100 };
    private readonly Label _statusLabel = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ProgressBar _progressBar = new() { Dock = DockStyle.Fill, Visible = false, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25 };
    private readonly Label _progressLabel = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Visible = false };
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 1000 };
    private readonly Stopwatch _stopwatch = new();
    private string _currentProgressMessage = string.Empty;
    private bool _operationInProgress;

    public MainForm()
    {
        Text = "CursorAI Backup Tool";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 560);
        Size = new Size(1000, 660);

        BuildLayout();
        ConfigureGrid();

        _backupButton.Click += async (_, _) => await BackupAsync();
        _restoreButton.Click += async (_, _) => await RestoreAsync();
        _deleteButton.Click += async (_, _) => await DeleteAsync();
        _refreshButton.Click += async (_, _) => await RefreshBackupsAsync();
        _elapsedTimer.Tick += (_, _) => UpdateProgressText();
        FormClosing += OnFormClosing;
        Shown += async (_, _) => await RefreshBackupsAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var title = new Label
        {
            Text = "CursorAI Backup Tool",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10)
        };
        controls.Controls.Add(_normalRadio);
        controls.Controls.Add(_fullRadio);
        controls.Controls.Add(new Label { Width = 16 });
        controls.Controls.Add(_backupButton);
        controls.Controls.Add(_restoreButton);
        controls.Controls.Add(_deleteButton);
        controls.Controls.Add(_refreshButton);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(controls, 0, 1);
        root.Controls.Add(_grid, 0, 2);
        root.Controls.Add(_progressLabel, 0, 3);
        root.Controls.Add(_progressBar, 0, 4);
        root.Controls.Add(_statusLabel, 0, 5);
        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        _grid.RowHeadersVisible = false;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "백업", DataPropertyName = nameof(BackupRecord.Name), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 42 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "종류", DataPropertyName = nameof(BackupRecord.Type), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "상태", DataPropertyName = nameof(BackupRecord.Status), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "크기", DataPropertyName = nameof(BackupRecord.SizeText), Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "생성 시각", DataPropertyName = nameof(BackupRecord.CreatedText), Width = 160 });
    }

    private async Task BackupAsync()
    {
        if (!EnsureCursorStoppedForOperation("백업")) return;

        if (_fullRadio.Checked)
        {
            var answer = MessageBox.Show(
                "전체 AI 백업은 두 WorkspaceStorage를 포함하므로 백업 용량이 크게 증가할 수 있습니다.\r\n계속하시겠습니까?",
                "전체 AI 백업",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        var result = await RunOperationAsync(
            "백업 준비 중...",
            progress => _service.CreateBackupAsync(_fullRadio.Checked, progress));

        ShowResult(result);
        await RefreshBackupsAsync();
    }

    private async Task RestoreAsync()
    {
        var record = SelectedBackup();
        if (record is null)
        {
            MessageBox.Show("복원할 백업을 선택하십시오.", "복원", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (record.IsIncomplete)
        {
            MessageBox.Show("완료되지 않은 백업은 복원할 수 없습니다.", "복원", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!EnsureCursorStoppedForOperation("복원")) return;

        var answer = MessageBox.Show(
            $"'{record.Name}' 백업으로 복원합니다.\r\n복원 전에 현재 상태의 안전 백업을 자동 생성합니다.\r\n계속하시겠습니까?",
            "복원 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        var result = await RunOperationAsync(
            "복원 준비 중...",
            progress => _service.RestoreAsync(record, progress));

        ShowResult(result);
        await RefreshBackupsAsync();
    }

    private async Task DeleteAsync()
    {
        var record = SelectedBackup();
        if (record is null)
        {
            MessageBox.Show("삭제할 백업을 선택하십시오.", "삭제", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"'{record.Name}' 백업을 완전히 삭제하시겠습니까?",
            "백업 삭제",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        SetBusy(true);
        SetStatus("삭제 중...");
        OperationResult result;
        try
        {
            result = await Task.Run(() => _service.DeleteBackup(record));
        }
        finally
        {
            SetBusy(false);
        }

        ShowResult(result);
        await RefreshBackupsAsync();
    }

    private async Task RefreshBackupsAsync()
    {
        try
        {
            SetStatus("백업 목록 확인 중...");
            var records = await _service.GetBackupsAsync();
            _grid.DataSource = records.ToList();
            SetStatus($"백업 {records.Count}개 · 저장 위치: {_service.BackupRoot}");
        }
        catch (Exception ex)
        {
            SetStatus("백업 목록을 읽지 못했습니다.");
            MessageBox.Show(ex.Message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool EnsureCursorStoppedForOperation(string operationName)
    {
        if (!_service.IsCursorRunning()) return true;

        var answer = MessageBox.Show(
            $"Cursor가 현재 실행 중입니다.\r\n안전한 {operationName}을 위해 Cursor를 자동 종료한 뒤 계속하시겠습니까?",
            "Cursor 실행 중",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return false;

        SetStatus("Cursor 종료 중...");
        _service.StopCursor();

        var waitUntil = DateTime.UtcNow.AddSeconds(5);
        while (_service.IsCursorRunning() && DateTime.UtcNow < waitUntil)
        {
            Application.DoEvents();
            Thread.Sleep(100);
        }

        if (_service.IsCursorRunning())
        {
            MessageBox.Show("Cursor를 완전히 종료하지 못했습니다. 작업을 시작하지 않습니다.", "Cursor 종료 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        return true;
    }

    private BackupRecord? SelectedBackup() => _grid.CurrentRow?.DataBoundItem as BackupRecord;

    private async Task<OperationResult> RunOperationAsync(
        string initialStatus,
        Func<IProgress<OperationProgress>, Task<OperationResult>> action)
    {
        SetBusy(true);
        BeginProgress(initialStatus);
        var progress = new Progress<OperationProgress>(p =>
        {
            _currentProgressMessage = p.Message;
            UpdateProgressText();
        });

        try
        {
            return await action(progress);
        }
        catch (Exception ex)
        {
            return new OperationResult(false, ex.Message);
        }
        finally
        {
            EndProgress();
            SetBusy(false);
        }
    }

    private void BeginProgress(string message)
    {
        _currentProgressMessage = message;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 25;
        _progressBar.Visible = true;
        _progressLabel.Visible = true;
        _stopwatch.Restart();
        _elapsedTimer.Start();
        UpdateProgressText();
        SetStatus(message);
    }

    private void EndProgress()
    {
        _elapsedTimer.Stop();
        _stopwatch.Stop();
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Visible = false;
        _progressLabel.Visible = false;
        _currentProgressMessage = string.Empty;
    }

    private void UpdateProgressText()
    {
        if (!_stopwatch.IsRunning && _stopwatch.Elapsed == TimeSpan.Zero) return;
        var elapsed = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
        _progressLabel.Text = $"{_currentProgressMessage}   ·   경과 {elapsed}";
        if (_progressBar.Visible) SetStatus($"작업 진행 중 · {_currentProgressMessage} · 경과 {elapsed}");
    }

    private void SetBusy(bool busy)
    {
        _operationInProgress = busy;
        _backupButton.Enabled = !busy;
        _restoreButton.Enabled = !busy;
        _deleteButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _normalRadio.Enabled = !busy;
        _fullRadio.Enabled = !busy;
        _grid.Enabled = !busy;
        ControlBox = !busy;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_operationInProgress) return;
        e.Cancel = true;
        MessageBox.Show("백업 또는 복원 작업이 진행 중입니다. 작업이 끝난 뒤 프로그램을 종료하십시오.", "작업 진행 중", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private static void ShowResult(OperationResult result)
    {
        MessageBox.Show(
            result.Message,
            result.Success ? "완료" : "실패",
            MessageBoxButtons.OK,
            result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    }
}
