using CommunityToolkit.Mvvm.ComponentModel;
using SmartBasket.Core.Configuration;

namespace SmartBasket.WPF.ViewModels.Settings;

/// <summary>
/// ViewModel для редактирования источника чеков
/// </summary>
public partial class ReceiptSourceViewModel : ObservableObject
{
    public ReceiptSourceViewModel() { }

    public ReceiptSourceViewModel(ReceiptSourceConfig config)
    {
        _originalName = config.Name;
        Name = config.Name;
        Type = config.Type;
        Parser = config.Parser;
        IsEnabled = config.IsEnabled;

        if (config.Email != null)
        {
            ImapServer = config.Email.ImapServer;
            ImapPort = config.Email.ImapPort;
            UseSsl = config.Email.UseSsl;
            Username = config.Email.Username;
            Password = config.Email.Password;
            SenderFilter = config.Email.SenderFilter ?? string.Empty;
            SubjectFilter = config.Email.SubjectFilter ?? string.Empty;
            Folder = config.Email.Folder;
            SearchDaysBack = config.Email.SearchDaysBack;
        }
    }

    private readonly string _originalName = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmailSource))]
    private SourceType _type = SourceType.Email;

    [ObservableProperty]
    private string _parser = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    // Email-specific properties
    [ObservableProperty]
    private string _imapServer = string.Empty;

    [ObservableProperty]
    private int _imapPort = 993;

    [ObservableProperty]
    private bool _useSsl = true;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _senderFilter = string.Empty;

    [ObservableProperty]
    private string _subjectFilter = string.Empty;

    [ObservableProperty]
    private string _folder = "INBOX";

    [ObservableProperty]
    private int _searchDaysBack = 30;

    public bool IsEmailSource => Type == SourceType.Email;

    public bool IsNew => string.IsNullOrEmpty(_originalName);

    /// <summary>
    /// Преобразование обратно в конфигурацию
    /// </summary>
    public ReceiptSourceConfig ToConfig()
    {
        var config = new ReceiptSourceConfig
        {
            Name = Name,
            Type = Type,
            Parser = Parser,
            IsEnabled = IsEnabled
        };

        if (Type == SourceType.Email)
        {
            config.Email = new EmailSourceConfig
            {
                ImapServer = ImapServer,
                ImapPort = ImapPort,
                UseSsl = UseSsl,
                Username = Username,
                Password = Password,
                SenderFilter = string.IsNullOrWhiteSpace(SenderFilter) ? null : SenderFilter,
                SubjectFilter = string.IsNullOrWhiteSpace(SubjectFilter) ? null : SubjectFilter,
                Folder = Folder,
                SearchDaysBack = SearchDaysBack
            };
        }

        return config;
    }
}
