using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.Services
{
    public class CacheService : ICacheService
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cache = new();
        private readonly ILogger<CacheService>? _logger;

        public CacheService(ILogger<CacheService>? logger = null)
        {
            _logger = logger;
        }

        public T? Get<T>(string key) where T : class
        {
            if (string.IsNullOrEmpty(key))
            {
                _logger?.LogWarning("Cache key is null or empty");
                return null;
            }

            if (_cache.TryGetValue(key, out var item))
            {
                if (item.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    _logger?.LogDebug("Cache item expired and removed: {Key}", key);
                    return null;
                }

                _logger?.LogDebug("Cache hit: {Key}", key);
                return item.Value as T;
            }

            _logger?.LogDebug("Cache miss: {Key}", key);
            return null;
        }

        public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class
        {
            if (string.IsNullOrEmpty(key))
            {
                _logger?.LogWarning("Cannot set cache with null or empty key");
                return;
            }

            if (value == null)
            {
                _logger?.LogWarning("Cannot set null value in cache for key: {Key}", key);
                return;
            }

            var item = new CacheItem(value, expiration);
            _cache.AddOrUpdate(key, item, (_, _) => item);
            _logger?.LogDebug("Cache item set: {Key} with expiration: {Expiration}", key, expiration);
        }

        public void Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                _logger?.LogWarning("Cannot remove cache with null or empty key");
                return;
            }

            if (_cache.TryRemove(key, out _))
            {
                _logger?.LogDebug("Cache item removed: {Key}", key);
            }
        }

        public void Clear()
        {
            var count = _cache.Count;
            _cache.Clear();
            _logger?.LogInformation("Cache cleared, removed {Count} items", count);
        }

        private class CacheItem
        {
            public object Value { get; }
            public DateTime? ExpirationTime { get; }

            public CacheItem(object value, TimeSpan? expiration)
            {
                Value = value;
                ExpirationTime = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null;
            }

            public bool IsExpired => ExpirationTime.HasValue && DateTime.UtcNow > ExpirationTime.Value;
        }
    }
} 