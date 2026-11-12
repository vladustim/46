using System.Net;
using PostService.CommonTypes;
using PostService.Dtos;

namespace PostService.App;

public partial class PostingList : Form
{
    private readonly IPostServiceClient client = PostServiceClient.Shared;
    private bool busy;

    public PostingList()
    {
        InitializeComponent();
        gridPostings.AutoGenerateColumns = false;
        UpdateButtons();
    }

    private PostingGetDto? SelectedPosting =>
        gridPostings.SelectedRows.Count == 1
            ? gridPostings.SelectedRows[0].DataBoundItem as PostingGetDto
            : null;

    private async void PostingList_Load(object? sender, EventArgs e) =>
        await RunAsync(() => ReloadAsync());

    private async void btnRefresh_Click(object? sender, EventArgs e) =>
        await RunAsync(() => ReloadAsync());

    private async void btnNew_Click(object? sender, EventArgs e) =>
        await RunAsync(() => OpenEditorAsync(null));

    private async void btnEdit_Click(object? sender, EventArgs e)
    {
        if (SelectedPosting is { } selected)
        {
            await RunAsync(async () =>
            {
                PostingGetDto? posting = null;
                try
                {
                    await PostingRequestRetry.RunAsync(this,
                        async () => posting = await client.GetPostingByIdAsync(selected.Id));
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    await RefreshMissingPostingAsync();
                    return;
                }

                if (posting is not null)
                {
                    await OpenEditorAsync(posting);
                }
            });
        }
    }

    private async Task OpenEditorAsync(PostingGetDto? posting)
    {
        if (IsDisposed)
        {
            return;
        }

        using var editor = new PostingEditor(posting);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            await ReloadAsync("Зміни збережено. ");
        }
        else if (editor.NeedsRefresh)
        {
            await ReloadAsync();
        }
    }

    private async void btnDelete_Click(object? sender, EventArgs e)
    {
        if (SelectedPosting is not { } selected)
        {
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"Видалити відправлення № {selected.Id}?\nОтримувач: {selected.To}",
            "Підтвердження видалення", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        await RunAsync(async () =>
        {
            bool deleted;
            try
            {
                deleted = await PostingRequestRetry.RunAsync(this, () => client.DeletePostingAsync(selected.Id));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await RefreshMissingPostingAsync();
                return;
            }

            if (deleted)
            {
                await ReloadAsync("Відправлення видалено. ");
            }
            else if (!IsDisposed)
            {
                lblStatus.Text = "Видалення не підтверджено. Оновіть список.";
            }
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy)
        {
            return;
        }

        busy = true;
        UpdateButtons();
        try
        {
            await action();
        }
        finally
        {
            busy = false;
            if (!IsDisposed)
            {
                UpdateButtons();
            }
        }
    }

    private async Task ReloadAsync(string statusPrefix = "")
    {
        if (IsDisposed)
        {
            return;
        }

        int? selectedId = SelectedPosting?.Id;
        lblStatus.Text = "Завантаження відправлень…";
        PostingGetDto[] postings = [];
        bool completed = await PostingRequestRetry.RunAsync(this,
            async () => postings = await client.GetPostingsAsync());
        if (IsDisposed)
        {
            return;
        }

        if (!completed)
        {
            lblStatus.Text = statusPrefix + "Оновлення списку скасовано.";
            return;
        }

        gridPostings.DataSource = postings;
        foreach (DataGridViewRow row in gridPostings.Rows)
        {
            if (row.DataBoundItem is PostingGetDto posting && posting.Id == selectedId)
            {
                gridPostings.CurrentCell = row.Cells[colId.Index];
                break;
            }
        }

        lblStatus.Text = postings.Length == 0
            ? statusPrefix + "Список відправлень порожній."
            : statusPrefix + $"Усього відправлень: {postings.Length}";
    }

    private void gridPostings_SelectionChanged(object? sender, EventArgs e) => UpdateButtons();

    private void gridPostings_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex == colDeliveryType.Index && e.Value is DeliveryType type)
        {
            e.Value = DeliveryTypeLabels.GetLabel(type);
            e.FormattingApplied = true;
        }
    }

    private void UpdateButtons()
    {
        btnNew.Enabled = !busy;
        btnRefresh.Enabled = !busy;
        btnEdit.Enabled = !busy && SelectedPosting is not null;
        btnDelete.Enabled = btnEdit.Enabled;
    }

    private async Task RefreshMissingPostingAsync()
    {
        if (IsDisposed)
        {
            return;
        }

        MessageBox.Show(this, "Відправлення вже відсутнє. Список буде оновлено.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        await ReloadAsync();
    }
}
