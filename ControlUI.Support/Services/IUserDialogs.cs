namespace ControlUI.Support.Services;

public interface IUserDialogs
{
    string? OpenFile(string filter, string? title = null, string? directory = null, string? fileName = null);
    string? SaveFile(string filter, string extension, string fileName, string? title = null);
    string? OpenFolder(string title);
    bool Confirm(string message, string title);
}
