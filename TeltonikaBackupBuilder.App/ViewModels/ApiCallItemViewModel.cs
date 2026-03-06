using TeltonikaBackupBuilder.App.Services;

namespace TeltonikaBackupBuilder.App.ViewModels;

public sealed class ApiCallItemViewModel : ViewModelBase
{
    private string _status = "Geplant";
    private string _response = string.Empty;
    private bool _isSuccess;

    public ApiCallItemViewModel(PlannedApiCall call)
    {
        Call = call;
    }

    public PlannedApiCall Call { get; }

    public int Order => Call.Order;

    public string Name => Call.Name;

    public string Method => Call.Method;

    public string Path => Call.Path;

    public string RequestBody => Call.RequestBody ?? string.Empty;

    public ApiCallKind Kind => Call.Kind;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Response
    {
        get => _response;
        set => SetProperty(ref _response, value);
    }

    public bool IsSuccess
    {
        get => _isSuccess;
        set => SetProperty(ref _isSuccess, value);
    }
}
