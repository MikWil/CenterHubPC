namespace CenterHubNew.MVVM.Services
{
    public interface IConfigurationService
    {
        T? GetValue<T>(string key, T? defaultValue = default);
        void SetValue<T>(string key, T value);
        void Save();
        void Load();
    }
}
