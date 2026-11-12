using System.Globalization;
using System.Net;
using PostService.CommonTypes;
using PostService.Dtos;

namespace PostService.App;

public partial class PostingEditor : Form
{
    private static readonly CultureInfo Ukrainian = CultureInfo.GetCultureInfo("uk-UA");
    private readonly IPostServiceClient client = PostServiceClient.Shared;
    private readonly int? postingId;
    private readonly float declaredValue;
    private bool saving;
    private bool saveUnconfirmed;

    internal bool NeedsRefresh { get; private set; }

    public PostingEditor()
    {
        InitializeComponent();

        var options = Enum.GetValues<DeliveryType>()
            .Select(type => new DeliveryOption(type, DeliveryTypeLabels.GetLabel(type)))
            .ToArray();
        cmbDelivery.DisplayMember = nameof(DeliveryOption.Label);
        cmbDelivery.DataSource = options;
        cmbDelivery.SelectedIndexChanged += UpdatePrice;
        txtWeight.TextChanged += UpdatePrice;
        txtDepth.TextChanged += UpdatePrice;
        txtWidth.TextChanged += UpdatePrice;
        txtHeight.TextChanged += UpdatePrice;
    }

    internal PostingEditor(PostingGetDto? posting) : this()
    {
        if (posting is null)
        {
            return;
        }

        postingId = posting.Id;
        declaredValue = posting.Value;
        Text = "Редагування відправлення";
        lblTitle.Text = $"Редагування відправлення № {posting.Id}";
        txtFrom.Text = posting.From;
        txtTo.Text = posting.To;
        txtContent.Text = posting.Content;
        cmbDelivery.SelectedIndex = Array.IndexOf(Enum.GetValues<DeliveryType>(), posting.DeliveryType);
        txtWeight.Text = posting.Weight.ToString(Ukrainian);
        txtDepth.Text = posting.Depth.ToString(Ukrainian);
        txtWidth.Text = posting.Width.ToString(Ukrainian);
        txtHeight.Text = posting.Height.ToString(Ukrainian);
    }

    private void UpdatePrice(object? sender, EventArgs e)
    {
        lblBillableWeight.Text = "—";
        lblPrice.Text = "—";
        if (cmbDelivery.SelectedItem is not DeliveryOption delivery ||
            !TryReadNumber(txtWeight.Text, out float actualWeight) || actualWeight <= 0 ||
            !TryReadDimension(txtDepth.Text, out int depth) ||
            !TryReadDimension(txtWidth.Text, out int width) ||
            !TryReadDimension(txtHeight.Text, out int height))
        {
            return;
        }

        float billableWeight = DeliveryPrice.GetBillableWeight(actualWeight, depth, width, height);
        float price = DeliveryPrice.Calculate(billableWeight, delivery.Type);
        lblBillableWeight.Text = $"{billableWeight.ToString("0.####", Ukrainian)} кг";
        if (float.IsFinite(price))
        {
            lblPrice.Text = $"{price.ToString("N2", Ukrainian)} грн";
        }
    }

    private async void btnSave_Click(object? sender, EventArgs e)
    {
        if (saving || saveUnconfirmed || ReadInput() is not { } data)
        {
            return;
        }

        saving = true;
        btnSave.Enabled = false;
        btnCancel.Enabled = false;
        pnlFields.Enabled = false;
        lblFeedback.Text = "Збереження…";
        try
        {
            bool completed = await PostingRequestRetry.RunAsync(this, () => SaveAsync(data),
                repeatable: postingId.HasValue);
            if (completed)
            {
                saving = false;
                DialogResult = DialogResult.OK;
            }
            else
            {
                lblFeedback.Text = "Збереження скасовано.";
            }
        }
        catch (PostingValidationException ex)
        {
            ShowValidationErrors(ex);
        }
        catch (HttpRequestException ex) when (postingId.HasValue && ex.StatusCode == HttpStatusCode.NotFound)
        {
            MessageBox.Show(this, "Відправлення вже відсутнє. Список буде оновлено.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            NeedsRefresh = true;
            saving = false;
            DialogResult = DialogResult.Cancel;
        }
        catch (HttpRequestException ex) when (postingId is null &&
            ex.StatusCode is null or HttpStatusCode.ServiceUnavailable)
        {
            ShowUnconfirmedCreate();
        }
        catch (OperationCanceledException ex) when (postingId is null && ex.InnerException is TimeoutException)
        {
            ShowUnconfirmedCreate();
        }
        finally
        {
            saving = false;
            if (!IsDisposed)
            {
                btnSave.Enabled = !saveUnconfirmed;
                btnCancel.Enabled = true;
                pnlFields.Enabled = true;
            }
        }
    }

    private Task<PostingGetDto> SaveAsync(PostingPostDto data)
    {
        if (postingId is not { } id)
        {
            return client.CreatePostingAsync(data);
        }

        return client.UpdatePostingAsync(id, new PostingPutDto
        {
            Id = id,
            From = data.From,
            To = data.To,
            Content = data.Content,
            DeliveryType = data.DeliveryType,
            Weight = data.Weight,
            Depth = data.Depth,
            Width = data.Width,
            Height = data.Height,
            Value = declaredValue
        });
    }

    private void ShowValidationErrors(PostingValidationException exception)
    {
        lblFeedback.Text = exception.Message;
        foreach (var (field, messages) in exception.Errors)
        {
            Control? control = field.ToUpperInvariant() switch
            {
                "FROM" => txtFrom,
                "TO" => txtTo,
                "CONTENT" => txtContent,
                "DELIVERYTYPE" => cmbDelivery,
                "WEIGHT" => txtWeight,
                "DEPTH" => txtDepth,
                "WIDTH" => txtWidth,
                "HEIGHT" => txtHeight,
                _ => null
            };
            if (control is not null)
            {
                errors.SetError(control, string.Join(Environment.NewLine, messages));
            }
        }

        MessageBox.Show(this, string.Join(Environment.NewLine, exception.Errors.Values.SelectMany(messages => messages)),
            "Перевірте введені дані", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ShowUnconfirmedCreate()
    {
        saveUnconfirmed = NeedsRefresh = true;
        lblFeedback.Text = "Не вдалося підтвердити створення.";
        MessageBox.Show(this, "Поверніться до списку, щоб перевірити, чи створено відправлення.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private PostingPostDto? ReadInput()
    {
        errors.Clear();
        lblFeedback.Text = string.Empty;
        Control? firstInvalid = null;

        void Check(Control control, bool valid, string message)
        {
            if (!valid)
            {
                errors.SetError(control, message);
                firstInvalid ??= control;
            }
        }

        string from = txtFrom.Text.Trim();
        string to = txtTo.Text.Trim();
        string content = txtContent.Text.Trim();
        var delivery = cmbDelivery.SelectedItem as DeliveryOption;

        Check(txtFrom, from.Length > 0, "Вкажіть відправника.");
        Check(txtTo, to.Length > 0, "Вкажіть отримувача.");
        Check(txtContent, content.Length > 0, "Опишіть вміст відправлення.");
        Check(cmbDelivery, delivery is not null, "Виберіть тип доставки.");
        Check(txtWeight, TryReadNumber(txtWeight.Text, out float weight) && weight > 0,
            "Введіть вагу більше 0 кг.");
        Check(txtDepth, TryReadDimension(txtDepth.Text, out int depth),
            "Введіть довжину як ціле число більше 0.");
        Check(txtWidth, TryReadDimension(txtWidth.Text, out int width),
            "Введіть ширину як ціле число більше 0.");
        Check(txtHeight, TryReadDimension(txtHeight.Text, out int height),
            "Введіть висоту як ціле число більше 0.");
        if (firstInvalid is null && delivery is not null)
        {
            float billableWeight = DeliveryPrice.GetBillableWeight(weight, depth, width, height);
            Check(txtWeight, float.IsFinite(DeliveryPrice.Calculate(billableWeight, delivery.Type)),
                "Вага завелика для розрахунку вартості.");
        }

        if (firstInvalid is not null)
        {
            lblFeedback.Text = errors.GetError(firstInvalid);
            firstInvalid.Focus();
            return null;
        }

        return new PostingPostDto
        {
            From = from,
            To = to,
            Content = content,
            DeliveryType = delivery!.Type,
            Weight = weight,
            Depth = depth,
            Width = width,
            Height = height
        };
    }

    private static bool TryReadNumber(string text, out float value) =>
        float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

    private static bool TryReadDimension(string text, out int value) =>
        int.TryParse(text, out value) && value > 0;

    private void PostingEditor_FormClosing(object? sender, FormClosingEventArgs e)
    {
        e.Cancel = saving;
    }

    private sealed record DeliveryOption(DeliveryType Type, string Label);
}
