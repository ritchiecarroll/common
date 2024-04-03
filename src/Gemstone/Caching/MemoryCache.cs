﻿//******************************************************************************************************
//  MemoryCache.cs - Gbtc
//
//  Copyright © 2024, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  03/17/2024 - Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************
// ReSharper disable StaticMemberInGenericType

using System;
using System.Runtime.Caching;
using Gemstone.TypeExtensions;

namespace Gemstone.Caching;

/// <summary>
/// Represents a generic memory cache for a specific type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">Type of value to cache.</typeparam>
/// <remarks>
/// Each type T should be unique unless cache can be safely shared.
/// </remarks>
internal static class MemoryCache<T>
{
    // Desired use case is one static MemoryCache per type T:
    private static readonly MemoryCache s_memoryCache;

    static MemoryCache()
    {
        // Reflected type name is used to ensure unique cache name for generic types
        string cacheName = $"{nameof(Gemstone)}Cache:{typeof(T).GetReflectedTypeName()}";
        s_memoryCache = new MemoryCache(cacheName);
    }

    /// <summary>
    /// Try to get a value from the memory cache.
    /// </summary>
    /// <param name="cacheName">Name to use as cache key -- this should be unique per <typeparamref name="T"/>.</param>
    /// <param name="value">Value from cache if already cached; otherwise, default value for <typeparamref name="T"/>.</param>
    /// <returns></returns>
    public static bool TryGet(string cacheName, out T? value)
    {
        if (s_memoryCache.Get(cacheName) is not Lazy<T> cachedValue)
        {
            value = default;
            return false;
        }

        value = cachedValue.Value;
        return true;
    }

    /// <summary>
    /// Gets or adds a value, based on result of <paramref name="valueFactory"/>, to the memory cache. Cache defaults to a 1-minute expiration.
    /// </summary>
    /// <param name="cacheName">Name to use as cache key -- this should be unique per <typeparamref name="T"/>.</param>
    /// <param name="valueFactory">Function to generate value to add to cache -- only called if value is not already cached.</param>
    /// <returns>
    /// Value from cache if already cached; otherwise, new value generated by <paramref name="valueFactory"/>.
    /// </returns>
    public static T GetOrAdd(string cacheName, Func<T> valueFactory)
    {
        return GetOrAdd(cacheName, 1.0D, valueFactory);
    }

    /// <summary>
    /// Gets or adds a value, based on result of <paramref name="valueFactory"/>, to the memory cache.
    /// </summary>
    /// <param name="cacheName">Name to use as cache key -- this should be unique per <typeparamref name="T"/>.</param>
    /// <param name="expirationTime">Expiration time, in minutes, for cached value.</param>
    /// <param name="valueFactory">Function to generate value to add to cache -- only called if value is not already cached.</param>
    /// <returns>
    /// Value from cache if already cached; otherwise, new value generated by <paramref name="valueFactory"/>.
    /// </returns>
    public static T GetOrAdd(string cacheName, double expirationTime, Func<T> valueFactory)
    {
        Lazy<T> newValue = new(valueFactory);
        Lazy<T>? oldValue;

        try
        {
            // Race condition exists here such that memory cache being referenced may
            // be disposed between access and method invocation - hence the try/catch
            oldValue = s_memoryCache.AddOrGetExisting(cacheName, newValue, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(expirationTime) }) as Lazy<T>;
        }
        catch
        {
            oldValue = null;
        }

        try
        {
            return (oldValue ?? newValue).Value;
        }
        catch
        {
            s_memoryCache.Remove(cacheName);
            throw;
        }
    }

    /// <summary>
    /// Removes a value from the memory cache.
    /// </summary>
    /// <param name="cacheName">Specific named memory cache instance to remove from cache.</param>
    public static void Remove(string cacheName)
    {
        s_memoryCache.Remove(cacheName);
    }
}
