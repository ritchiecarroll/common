﻿//******************************************************************************************************
//  Settings.cs - Gbtc
//
//  Copyright © 2020, Grid Protection Alliance.  All Rights Reserved.
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
//  03/14/2024 - J. Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************
// ReSharper disable NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using Gemstone.Configuration.AppSettings;
using Gemstone.Configuration.INIConfigurationExtensions;
using Gemstone.Configuration.ReadOnly;
using Gemstone.Threading.SynchronizedOperations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Ini;
using static Gemstone.Configuration.INIConfigurationHelpers;

namespace Gemstone.Configuration;

/// <summary>
/// Defines system settings for an application.
/// </summary>
public class Settings : DynamicObject
{
    /// <summary>
    /// Defines the configuration section name for system settings.
    /// </summary>
    public const string SystemSettingsCategory = nameof(System);

    private readonly ConcurrentDictionary<string, SettingsSection> m_sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(string key, object? defaultValue, string description, string[]? switchMappings)> m_definedSettings = [];
    private readonly List<IConfigurationProvider> m_configurationProviders = [];
    private readonly ShortSynchronizedOperation m_saveOperation;

    /// <summary>
    /// Creates a new <see cref="Settings"/> instance.
    /// </summary>
    public Settings()
    {
        Instance ??= this;
        m_saveOperation = new ShortSynchronizedOperation(SaveSections, ex => LibraryEvents.OnSuppressedException(this, ex));
    }

    /// <summary>
    /// Gets or sets the source <see cref="IConfiguration"/> for settings.
    /// </summary>
    public IConfiguration? Configuration { get; set; }

    /// <summary>
    /// Gets or sets flag that determines if INI file should be used for settings.
    /// </summary>
    public bool UseINIFile { get; set; }

    /// <summary>
    /// Gets or sets flag that determines if SQLite should be used for settings.
    /// </summary>
    public bool UseSQLite { get; set; }

    /// <summary>
    /// Gets or sets flag that determines if INI description lines should be split into multiple lines.
    /// </summary>
    public bool SplitINIDescriptionLines { get; set; }

    /// <summary>
    /// Gets the names for the settings sections.
    /// </summary>
    public string[] SectionNames => m_sections.Keys.ToArray();

    /// <summary>
    /// Gets the sections count for the settings.
    /// </summary>
    public int Count => m_sections.Count;

    /// <summary>
    /// Gets the command line switch mappings for <see cref="Settings"/>.
    /// </summary>
    public Dictionary<string, string> SwitchMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override IEnumerable<string> GetDynamicMemberNames() => m_sections.Keys;

    /// <summary>
    /// Gets the <see cref="SettingsSection"/> for the specified key.
    /// </summary>
    /// <param name="key">Section key.</param>
    public SettingsSection this[string key] => m_sections.GetOrAdd(key, _ => new SettingsSection(this, key));

    /// <summary>
    /// Gets flag that determines if any settings have been changed.
    /// </summary>
    public bool IsDirty
    {
        get => m_sections.Values.Any(section => section.IsDirty);
        private set
        {
            foreach (SettingsSection section in m_sections.Values)
                section.IsDirty = value;
        }
    }

    /// <summary>
    /// Attempts to bind the <see cref="Settings"/> instance to configuration values by matching property
    /// names against configuration keys recursively.
    /// </summary>
    /// <param name="builder">Configuration builder used to bind settings.</param>
    public void Bind(IConfigurationBuilder builder)
    {
        // Build a new configuration with keys and values from the set of providers
        // registered in builder sources - we call this instead of directly using
        // the 'Build()' method on the config builder so providers can be cached
        foreach (IConfigurationSource source in builder.Sources)
        {
            IConfigurationProvider provider = source.Build(builder);
            m_configurationProviders.Add(provider);
        }

        // Cache configuration root
        Configuration = new ConfigurationRoot(m_configurationProviders);

        // Load settings from configuration sources hierarchy
        foreach (IConfigurationSection configSection in Configuration.GetChildren())
        {
            SettingsSection section = this[configSection.Key];

            foreach (IConfigurationSection entry in configSection.GetChildren())
                section[entry.Key] = entry.Value;

            section.ConfigurationSection = configSection;
            section.IsDirty = false;
        }
    }

    /// <inheritdoc/>
    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        string key = binder.Name;

        // If you try to get a value of a property that is
        // not defined in the class, this method is called.
        result = m_sections.GetOrAdd(key, _ => new SettingsSection(this, key));

        return true;
    }

    /// <inheritdoc/>
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        // If you try to set a value of a property that is
        // not defined in the class, this method is called.
        if (value is not SettingsSection section)
            return false;

        m_sections[binder.Name] = section;

        return true;
    }

    /// <inheritdoc/>
    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
    {
        if (indexes.Length != 1)
            throw new ArgumentException($"{nameof(Settings)} indexer requires a single index representing string name of settings section.");

        string key = indexes[0].ToString()!;

        result = m_sections.GetOrAdd(key, _ => new SettingsSection(this, key));

        return true;
    }

    /// <inheritdoc/>
    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value)
    {
        if (indexes.Length != 1)
            throw new ArgumentException($"{nameof(Settings)} indexer requires a single index representing string name of settings section.");

        if (value is not SettingsSection section)
            return false;

        m_sections[indexes[0].ToString()!] = section;

        return true;
    }

    /// <summary>
    /// Configures the <see cref="IAppSettingsBuilder"/> for <see cref="Settings"/>.
    /// </summary>
    /// <param name="builder">Builder used to configure settings.</param>
    public void ConfigureAppSettings(IAppSettingsBuilder builder)
    {
        foreach ((string key, object? defaultValue, string description, string[]? switchMappings) in m_definedSettings)
        {
            builder.Add(key, defaultValue?.ToString() ?? "", description);

            if (switchMappings is null || switchMappings.Length == 0)
                continue;

            foreach (string switchMapping in switchMappings)
                SwitchMappings[switchMapping] = key;
        }
    }

    // Defines application settings for the specified section key
    internal void DefineSetting(string key, string defaultValue, string description, string[]? switchMappings)
    {
        m_definedSettings.Add((key, defaultValue, description, switchMappings));
    }

    /// <summary>
    /// Saves any changed settings.
    /// </summary>
    /// <param name="waitForSave">Determines if save operation should wait for completion.</param>
    public void Save(bool waitForSave)
    {
        if (!IsDirty)
            return;

        if (waitForSave)
            m_saveOperation.Run(true);
        else
            m_saveOperation.RunAsync();

        IsDirty = false;
    }

    private void SaveSections()
    {
        try
        {
            foreach (IConfigurationProvider provider in m_configurationProviders)
            {
                if (provider is ReadOnlyConfigurationProvider readOnlyProvider)
                {
                    if (readOnlyProvider.Provider is not IniConfigurationProvider)
                        continue;

                    // Handle INI file as a special case, writing entire file contents on save
                    string contents = Configuration!.GenerateINIFileContents(true, SplitINIDescriptionLines);
                    string iniFilePath = GetINIFilePath("settings.ini");
                    using TextWriter writer = GetINIFileWriter(iniFilePath);
                    writer.Write(contents);
                }
                else
                {
                    foreach (SettingsSection section in m_sections.Values)
                    {
                        if (!section.IsDirty)
                            continue;

                        // Update configuration provider with each setting - in the case of
                        // SQLite, this will update the configuration database contents
                        foreach (string key in section.Keys)
                            provider.Set($"{section.Name}:{key}", section.ConfigurationSection[key]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed while trying to save configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves any changed settings.
    /// </summary>
    /// <param name="settings">Settings instance.</param>
    /// <remarks>
    /// This method will not return until the save operation has completed.
    /// </remarks>
    public static void Save(Settings? settings = null)
    {
        (settings ?? Instance).Save(true);
    }

    /// <summary>
    /// Gets the default instance of <see cref="Settings"/>.
    /// </summary>
    public static Settings Instance { get; private set; } = default!;

    /// <summary>
    /// Gets the default instance of <see cref="Settings"/> as a dynamic object.
    /// </summary>
    /// <returns>Default instance of <see cref="Settings"/> as a dynamic object.</returns>
    /// <exception cref="InvalidOperationException">Settings have not been initialized.</exception>
    public static dynamic Default => Instance ?? throw new InvalidOperationException("Settings have not been initialized.");

    /// <summary>
    /// Updates the default instance of <see cref="Settings"/>.
    /// </summary>
    /// <param name="settings">New default instance of <see cref="Settings"/>.</param>
    /// <remarks>
    /// This changes the default singleton instance of <see cref="Settings"/> to the specified instance.
    /// Use this method with caution as it can lead to unexpected behavior if the default instance is changed.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void UpdateInstance(Settings settings)
    {
        Instance = settings;
    }
}
