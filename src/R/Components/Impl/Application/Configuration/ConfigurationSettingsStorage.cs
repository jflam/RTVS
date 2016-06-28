﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using static System.FormattableString;

namespace Microsoft.R.Components.Application.Configuration {
    /// <summary>
    /// Storage of the R application settings. Settings are written
    /// into R file as assignment statements such as 'name &lt;- value'.
    /// Value can be string or an expression. The difference is that
    /// string values are quoted when written into the file and expressions
    /// are written as is.
    /// </summary>
    public sealed class ConfigurationSettingsStorage : IConfigurationSettingsStorage {
        public ConfigurationSettingsCollection Load(Stream stream) {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Persists settings into R file. Settings are written
        /// into R file as assignment statements such as 'name &lt;- value'.
        /// Value
        /// </summary>
        public void Save(ConfigurationSettingsCollection settings, Stream stream) {
            var sw = new StreamWriter(stream);
            try {
                WriteHeader(sw);
                foreach (var s in settings) {
                    var v = FormatValue(s);
                    if (!string.IsNullOrWhiteSpace(v)) {
                        WriteAttributes(s, sw);
                        sw.WriteLine(Invariant($"{s.Name} <- {v}"));
                        sw.WriteLine(string.Empty);
                    }
                }
            } finally {
                sw.Close();
            }
        }

        private void WriteHeader(StreamWriter sw) {
            sw.WriteLine("# Application settings file.");
            sw.WriteLine(string.Format(CultureInfo.CurrentCulture, "File content was generated on {0}", DateTime.Now));
            sw.WriteLine(string.Empty);
        }

        private void WriteAttributes(IConfigurationSetting s, StreamWriter sw) {
            foreach (var attName in s.Attributes.Keys) {
                var value = s.Attributes[attName];
                if (!string.IsNullOrEmpty(value)) {
                    sw.WriteLine(Invariant($"# [{attName}] {value}"));
                }
            }
        }

        private string FormatValue(IConfigurationSetting s) {
            if (s.ValueType == ConfigurationSettingValueType.String) {
                var hasSingleQuotes = s.Value.IndexOf('\'') >= 0;
                var hasDoubleQuotes = s.Value.IndexOf('\"') >= 0;
                if (hasSingleQuotes && !hasDoubleQuotes) {
                    return Invariant($"\"s\"");
                } else if (!hasSingleQuotes) {
                    return Invariant($"'s'");
                }
                // TODO: Resources.ConfigurationError_Quotes; ?
            }
            return s.Value;
        }
    }
}
