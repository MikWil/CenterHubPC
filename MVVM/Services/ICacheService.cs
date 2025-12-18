using System;

namespace CenterHubNew.MVVM.Services
{
    public interface ICacheService
    {
        T? Get<T>(string key) where T : class;
        void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class;
        void Remove(string key);
        void Clear();
    }
}
