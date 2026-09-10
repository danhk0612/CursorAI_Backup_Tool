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

    public MainForm()
    {
        Text = "CursorAI Backup Tool";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 520);
        Size = new Size(1000, 620);

        BuildLayout();
        ConfigureGrid();

        _backupButton.Click += async (_, _) => await BackupAsync();
        _restoreButton.Click += async (_, _) => await RestoreAsync();
        _deleteButton.Click += async (_, _) => await DeleteAsync();
        _refreshButton.Click += async (_, _) => await RefreshBackupsAsync();
        Shown += async (_, _) => await RefreshBackupsAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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
        root.Controls.Add(_statusLabel, 0, 3);
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
        if (!EnsureCursorStopped()) return;

        if (_fullRadio.Checked)
        {
            var answer = MessageBox.Show(
                "전체 AI 백업은 workspaceStorage를 포함하므로 백업 용량이 크게 증가할 수 있습니다.\r\n계속하시겠습니까?",
                "전체 AI 백업",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        await RunBusyAsync("백업 중...", async () =>
        {
            var result = await _service.CreateBackupAsync(_fullRadio.Checked);
            ShowResult(result);
            await RefreshBackupsAsync();
        });
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
        if (!EnsureCursorStopped()) return;

        var answer = MessageBox.Show(
            $"'{record.Name}' 백업으로 복원합니다.\r\n복원 전에 현재 상태의 안전 백업을 자동 생성합니다.\r\n계속하시겠습니까?",
            "복원 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync("복원 중...", async () =>
        {
            var result = await _service.RestoreAsync(record);
            ShowResult(result);
            await RefreshBackupsAsync();
        });
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

        await RunBusyAsync("삭제 중...", async () =>
        {
            var result = await Task.Run(() => _service.DeleteBackup(record));
            ShowResult(result);
            await RefreshBackupsAsync();
        });
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

    private bool EnsureCursorStopped()
    {
        if (!_service.IsCursorRunning()) return true;

        var answer = MessageBox.Show(
            "Cursor가 실행 중입니다. 안전한 작업을 위해 Cursor를 종료하시겠습니까?",
            "Cursor 실행 중",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return false;

        _service.StopCursor();
        if (_service.IsCursorRunning())
        {
            MessageBox.Show("Cursor를 완전히 종료하지 못했습니다. 직접 종료한 뒤 다시 시도하십시오.", "Cursor 종료 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        return true;
    }

    private BackupRecord? SelectedBackup() => _grid.CurrentRow?.DataBoundItem as BackupRecord;

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        SetBusy(true);
        SetStatus(status);
        try { await action(); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _backupButton.Enabled = !busy;
        _restoreButton.Enabled = !busy;
        _deleteButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _normalRadio.Enabled = !busy;
        _fullRadio.Enabled = !busy;
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
